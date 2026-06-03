using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Bitbound.Analyzers.MemberOrder;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MemberOrderCodeFixProvider)), Shared]
public class MemberOrderCodeFixProvider : CodeFixProvider
{
  public sealed override ImmutableArray<string> FixableDiagnosticIds => [MemberOrderAnalyzer.DiagnosticId];

  public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

  public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
  {
    var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

    if (root is null)
    {
      return;
    }

    var diagnostic = context.Diagnostics.First();
    var diagnosticSpan = diagnostic.Location.SourceSpan;

    // The diagnostic is on the identifier token. Find the MemberDeclarationSyntax
    // containing the token, then find its parent TypeDeclarationSyntax.
    // This works correctly for both regular members (fields, methods) and nested types.
    var member = root.FindToken(diagnosticSpan.Start)
      .Parent?.AncestorsAndSelf()
      .OfType<MemberDeclarationSyntax>()
      .FirstOrDefault();

    if (member?.Parent is not TypeDeclarationSyntax typeDecl)
    {
      // It might be a member in a compilation unit (e.g. global using)
      // For this analyzer, we only care about members within a type.
      return;
    }

    context.RegisterCodeFix(
        CodeAction.Create(
            title: CodeFixResources.CodeFixTitle,
            createChangedSolution: c => SortMembersAsync(context.Document, typeDecl, root, c),
            equivalenceKey: nameof(CodeFixResources.CodeFixTitle)),
        diagnostic);
  }

  private async Task<Solution> SortMembersAsync(Document document, TypeDeclarationSyntax typeDecl, SyntaxNode originalRoot, CancellationToken cancellationToken)
  {
    var members = typeDecl.Members;

    var sortedMembers = members
      .OrderBy(m => MemberOrderAnalyzer.GetMemberOrder(m).MemberType)
      .ThenBy(m => MemberOrderAnalyzer.GetMemberOrder(m).Accessibility)
      .ThenBy(m => MemberOrderAnalyzer.GetMemberOrder(m).ExternOrder)
      .ThenBy(m => MemberOrderAnalyzer.GetMemberOrder(m).StaticInstance)
      // Tie-break: alphabetical by identifier, ignoring case
      .ThenBy(m => MemberOrderAnalyzer.GetIdentifier(m).ValueText, StringComparer.OrdinalIgnoreCase)
      .ThenBy(m => MemberOrderAnalyzer.GetIdentifier(m).ValueText, StringComparer.Ordinal)
      .ToList();

    CopyWhiteSpace(ref members, sortedMembers, typeDecl);

    // Use WithMembers to create a new type declaration with sorted members
    var newTypeDecl = typeDecl.WithMembers(SyntaxFactory.List(sortedMembers));

    var newRoot = originalRoot.ReplaceNode(typeDecl, newTypeDecl);
    if (newRoot == null)
    {
      return document.Project.Solution;
    }

    return document.WithSyntaxRoot(newRoot).Project.Solution;
  }

  private static void CopyWhiteSpace(ref SyntaxList<MemberDeclarationSyntax> members, List<MemberDeclarationSyntax> sortedMembers, TypeDeclarationSyntax typeDecl)
  {
    if (members.Count == 0) return;

    bool isInterface = typeDecl is InterfaceDeclarationSyntax;

    // Detect the line ending style used in the existing code
    var endOfLineTrivia = DetectLineEndingTrivia(members);

    // Step 1: Normalize trailing trivia on every member (remove excessive newlines).
    for (int i = 0; i < sortedMembers.Count; i++)
    {
      var member = sortedMembers[i];
      var trailingTrivia = member.GetTrailingTrivia();

      var newTrailing = new List<SyntaxTrivia>();
      bool hasNewline = false;

      foreach (var t in trailingTrivia)
      {
        if (t.IsKind(SyntaxKind.EndOfLineTrivia))
        {
          if (!hasNewline)
          {
            newTrailing.Add(t);
            hasNewline = true;
          }
          // Skip additional newlines
        }
        else if (t.IsKind(SyntaxKind.WhitespaceTrivia))
        {
          // Keep whitespace only before the first newline
          if (!hasNewline)
          {
            newTrailing.Add(t);
          }
        }
        else
        {
          // Non-whitespace trivia (e.g., comments) always kept
          newTrailing.Add(t);
        }
      }

      sortedMembers[i] = member.WithTrailingTrivia(SyntaxFactory.TriviaList(newTrailing));
    }

    // Step 2: Adjust spacing between members.
    // We only skip the INITIAL whitespace/newlines from each member's leading trivia,
    // preserving everything after (comments, attributes, and their surrounding structure).
    for (int i = 1; i < sortedMembers.Count; i++)
    {
      var prevMember = sortedMembers[i - 1];
      var currMember = sortedMembers[i];

      var prevOrder = MemberOrderAnalyzer.GetMemberOrder(prevMember);
      var currentOrder = MemberOrderAnalyzer.GetMemberOrder(currMember);

      bool differentGroup = prevOrder.MemberType != currentOrder.MemberType ||
                            prevOrder.Accessibility != currentOrder.Accessibility ||
                            prevOrder.ExternOrder != currentOrder.ExternOrder ||
                            prevOrder.StaticInstance != currentOrder.StaticInstance;

      // Methods should always have spacing between them in classes, but not in interfaces
      bool isMethod = currMember.Kind() == SyntaxKind.MethodDeclaration;
      bool prevIsMethod = prevMember.Kind() == SyntaxKind.MethodDeclaration;
      bool bothMethods = isMethod && prevIsMethod && !isInterface;

      var prevTrailing = prevMember.GetTrailingTrivia();
      bool prevHasNewline = prevTrailing.Any(t => t.IsKind(SyntaxKind.EndOfLineTrivia));

      // Find where the initial whitespace/newlines end in the current member's leading trivia
      var existingTrivia = currMember.GetLeadingTrivia();
      var skipCount = 0;
      SyntaxTrivia? indentationWhitespace = null;

      foreach (var t in existingTrivia)
      {
        if (t.IsKind(SyntaxKind.WhitespaceTrivia))
        {
          indentationWhitespace = t;
          skipCount++;
        }
        else if (t.IsKind(SyntaxKind.EndOfLineTrivia))
        {
          indentationWhitespace = null;
          skipCount++;
        }
        else
        {
          // Found non-whitespace trivia (e.g., comment, attribute) - stop here
          break;
        }
      }

      int newlinesNeeded;
      if (differentGroup)
      {
        // Want 1 blank line (2 newlines total)
        newlinesNeeded = prevHasNewline ? 1 : 2;
      }
      else if (bothMethods)
      {
        // Methods should always have 1 blank line between them in classes
        newlinesNeeded = prevHasNewline ? 1 : 2;
      }
      else
      {
        // Want 0 blank lines (1 newline total)
        newlinesNeeded = prevHasNewline ? 0 : 1;
      }

      var newTriviaList = new List<SyntaxTrivia>();
      for (int k = 0; k < newlinesNeeded; k++)
      {
        newTriviaList.Add(endOfLineTrivia);
      }

      // Add back the indentation whitespace if it exists
      if (indentationWhitespace.HasValue)
      {
        newTriviaList.Add(indentationWhitespace.Value);
      }

      // Add back all non-whitespace trivia (comments, attributes, etc.)
      // along with their surrounding whitespace/newlines that come after the initial prefix
      for (int j = skipCount; j < existingTrivia.Count; j++)
      {
        newTriviaList.Add(existingTrivia[j]);
      }

      sortedMembers[i] = currMember.WithLeadingTrivia(SyntaxFactory.TriviaList(newTriviaList));
    }

    // Step 3: Copy leading whitespace from original first member to sorted first member.
    // This ensures the first member always has the correct opening-brace-to-member whitespace.
    var originalFirst = members[0];
    var reorderedFirst = sortedMembers[0];
    if (originalFirst != reorderedFirst)
    {
      sortedMembers[0] = FixLeadingTrivia(reorderedFirst, originalFirst, endOfLineTrivia);
    }

    // Step 4: Copy trailing trivia from original last member to sorted last member.
    int lastIdx = sortedMembers.Count - 1;
    var originalLast = members[lastIdx];
    var reorderedLast = sortedMembers[lastIdx];
    if (originalLast != reorderedLast)
    {
      sortedMembers[lastIdx] = reorderedLast.WithTrailingTrivia(originalLast.GetTrailingTrivia());
    }
  }

  private static MemberDeclarationSyntax FixLeadingTrivia(MemberDeclarationSyntax target, MemberDeclarationSyntax source, SyntaxTrivia endOfLineTrivia)
  {
    // If the target has non-whitespace leading trivia (e.g., XML doc comments, regular comments),
    // preserve it as-is. The indentation of those comments should be maintained.
    foreach (var t in target.GetLeadingTrivia())
    {
      if (!t.IsKind(SyntaxKind.WhitespaceTrivia) && !t.IsKind(SyntaxKind.EndOfLineTrivia))
      {
        return target;
      }
    }

    // Collect the indentation pattern from source (newlines + leading whitespace).
    var sourceWhitespace = new List<SyntaxTrivia>();
    foreach (var t in source.GetLeadingTrivia())
    {
      if (t.IsKind(SyntaxKind.WhitespaceTrivia) || t.IsKind(SyntaxKind.EndOfLineTrivia))
        sourceWhitespace.Add(t);
      else
        break;
    }

    // Strip all existing leading trivia from target, then prepend the source's whitespace.
    // This removes any original indentation while preserving comments/xml docs attached to the target.
    return target.WithLeadingTrivia(SyntaxFactory.TriviaList(sourceWhitespace));
  }

  private static SyntaxTrivia DetectLineEndingTrivia(SyntaxList<MemberDeclarationSyntax> members)
  {
    // Search through all members to find the first EndOfLineTrivia
    foreach (var member in members)
    {
      var leadingTrivia = member.GetLeadingTrivia();
      foreach (var trivia in leadingTrivia)
      {
        if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
        {
          return trivia;
        }
      }

      var trailingTrivia = member.GetTrailingTrivia();
      foreach (var trivia in trailingTrivia)
      {
        if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
        {
          return trivia;
        }
      }
    }

    // Default to LF if no line endings found (Linux/Mac default)
    return SyntaxFactory.LineFeed;
  }
}