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

    // The diagnostic is on the identifier token. We need to find the containing TypeDeclaration.
    var typeDecl = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();

    if (typeDecl is null)
    {
      // It might be a member in a compilation unit (e.g. global using)
      // For this analyzer, we only care about members within a type.
      return;
    }

    context.RegisterCodeFix(
        CodeAction.Create(
            title: CodeFixResources.CodeFixTitle,
            createChangedSolution: c => SortMembersAsync(context.Document, typeDecl, c),
            equivalenceKey: nameof(CodeFixResources.CodeFixTitle)),
        diagnostic);
  }

  private async Task<Solution> SortMembersAsync(Document document, TypeDeclarationSyntax typeDecl, CancellationToken cancellationToken)
  {
    var members = typeDecl.Members;

    var sortedMembers = members
        .OrderBy(m => MemberOrderAnalyzer.GetMemberOrder(m).MemberType)
        .ThenBy(m => MemberOrderAnalyzer.GetMemberOrder(m).Accessibility)
        .ThenBy(m => MemberOrderAnalyzer.GetMemberOrder(m).StaticInstance)
        // Tie-break: alphabetical by identifier to make ordering deterministic
        .ThenBy(m => MemberOrderAnalyzer.GetIdentifier(m).ValueText, StringComparer.Ordinal)
        .ToList();

    CopyWhiteSpace(ref members, sortedMembers);

    var newTypeDecl = typeDecl.WithMembers(SyntaxFactory.List(sortedMembers));

    var root = await document.GetSyntaxRootAsync(cancellationToken);
    var newRoot = root?.ReplaceNode(typeDecl, newTypeDecl);
    if (newRoot is null)
    {
      return document.Project.Solution;
    }
    return document.WithSyntaxRoot(newRoot).Project.Solution;
  }

  private static void CopyWhiteSpace(ref SyntaxList<MemberDeclarationSyntax> members, List<MemberDeclarationSyntax> sortedMembers)
  {
    if (members.Count == 0) return;

    // First pass: normalize trailing trivia to remove excessive newlines
    for (int i = 0; i < sortedMembers.Count; i++)
    {
      var member = sortedMembers[i];
      var trailingTrivia = member.GetTrailingTrivia();
      
      // Keep only the first newline in trailing trivia, remove any additional ones
      var newTrailingTrivia = new List<SyntaxTrivia>();
      bool hasNewline = false;
      
      foreach (var t in trailingTrivia)
      {
        if (t.IsKind(SyntaxKind.EndOfLineTrivia))
        {
          if (!hasNewline)
          {
            newTrailingTrivia.Add(t);
            hasNewline = true;
          }
          // Skip additional newlines
        }
        else if (t.IsKind(SyntaxKind.WhitespaceTrivia))
        {
          // Skip whitespace after newline
          if (!hasNewline)
          {
            newTrailingTrivia.Add(t);
          }
        }
        else
        {
          newTrailingTrivia.Add(t);
        }
      }
      
      sortedMembers[i] = member.WithTrailingTrivia(SyntaxFactory.TriviaList(newTrailingTrivia));
    }

    // Second pass: adjust spacing between members
    for (int i = 1; i < sortedMembers.Count; i++)
    {
      var prevMember = sortedMembers[i - 1];
      var currMember = sortedMembers[i];

      var prevOrder = MemberOrderAnalyzer.GetMemberOrder(prevMember);
      var currentOrder = MemberOrderAnalyzer.GetMemberOrder(currMember);

      bool differentGroup = prevOrder.MemberType != currentOrder.MemberType ||
                            prevOrder.Accessibility != currentOrder.Accessibility ||
                            prevOrder.StaticInstance != currentOrder.StaticInstance;

      // Methods should always have spacing between them, even in the same group
      bool isMethod = currMember.Kind() == SyntaxKind.MethodDeclaration;
      bool prevIsMethod = prevMember.Kind() == SyntaxKind.MethodDeclaration;
      bool bothMethods = isMethod && prevIsMethod;

      var prevTrailing = prevMember.GetTrailingTrivia();
      bool prevHasNewline = prevTrailing.Any(t => t.IsKind(SyntaxKind.EndOfLineTrivia));

      var existingTrivia = currMember.GetLeadingTrivia();
      var skipCount = 0;
      SyntaxTrivia? indentationWhitespace = null;

      // Skip all leading whitespace and newlines to find where non-whitespace trivia starts
      // Keep track of the last whitespace on the last line (the indentation)
      foreach (var t in existingTrivia)
      {
        if (t.IsKind(SyntaxKind.WhitespaceTrivia))
        {
          indentationWhitespace = t;
          skipCount++;
        }
        else if (t.IsKind(SyntaxKind.EndOfLineTrivia))
        {
          indentationWhitespace = null; // Reset when we hit a newline
          skipCount++;
        }
        else
        {
          // Found non-whitespace trivia (e.g., comment, attribute)
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
        // Methods should always have 1 blank line between them
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
        newTriviaList.Add(SyntaxFactory.CarriageReturnLineFeed);
      }

      // Add back the indentation whitespace if it exists
      if (indentationWhitespace.HasValue)
      {
        newTriviaList.Add(indentationWhitespace.Value);
      }

      // Add back all non-whitespace trivia (comments, attributes, etc.)
      for (int j = skipCount; j < existingTrivia.Count; j++)
      {
        newTriviaList.Add(existingTrivia[j]);
      }

      sortedMembers[i] = currMember.WithLeadingTrivia(SyntaxFactory.TriviaList(newTriviaList));
    }

    var originalFirst = members[0];
    var reorderedFirst = sortedMembers[0];
    if (originalFirst != reorderedFirst)
    {
      sortedMembers[0] = CopyLeadingWhitespace(reorderedFirst, originalFirst);
    }

    int lastIdx = sortedMembers.Count - 1;
    var originalLast = members[lastIdx];
    var reorderedLast = sortedMembers[lastIdx];

    if (originalLast != reorderedLast)
    {
      sortedMembers[lastIdx] = CopyTrailingTrivia(reorderedLast, originalLast);
    }
  }

  private static MemberDeclarationSyntax CopyLeadingWhitespace(MemberDeclarationSyntax target, MemberDeclarationSyntax source)
  {
    var sourceWhitespace = ExtractLeadingWhitespace(source.GetLeadingTrivia());
    var targetNonWhitespace = SkipLeadingWhitespace(target.GetLeadingTrivia());
    return target.WithLeadingTrivia(sourceWhitespace.Concat(targetNonWhitespace));
  }

  private static MemberDeclarationSyntax CopyTrailingTrivia(MemberDeclarationSyntax target, MemberDeclarationSyntax source)
      => target.WithTrailingTrivia(source.GetTrailingTrivia());

  private static IEnumerable<SyntaxTrivia> ExtractLeadingWhitespace(SyntaxTriviaList trivia)
  {
    foreach (var t in trivia)
    {
      if (t.IsKind(SyntaxKind.WhitespaceTrivia) || t.IsKind(SyntaxKind.EndOfLineTrivia))
        yield return t;
      else
        break;
    }
  }

  private static IEnumerable<SyntaxTrivia> SkipLeadingWhitespace(SyntaxTriviaList trivia)
  {
    bool foundNonWhitespace = false;
    foreach (var t in trivia)
    {
      if (!foundNonWhitespace && (t.IsKind(SyntaxKind.WhitespaceTrivia) || t.IsKind(SyntaxKind.EndOfLineTrivia)))
        continue;

      foundNonWhitespace = true;
      yield return t;
    }
  }
}