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
    var typeDecl = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().LastOrDefault();

    if (typeDecl is null)
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

    // Determine the common indentation from the original first member
    SyntaxTrivia? indentation = null;
    foreach (var t in members[0].GetLeadingTrivia())
    {
      if (t.IsKind(SyntaxKind.WhitespaceTrivia))
        indentation = t;
      else if (t.IsKind(SyntaxKind.EndOfLineTrivia))
        indentation = null;
    }

    // Step 1: Strip leading/trailing whitespace trivia from every member,
    // but preserve non-whitespace trivia (comments, attributes).
    for (int i = 0; i < sortedMembers.Count; i++)
    {
      var member = sortedMembers[i];

      var preservedLeading = member.GetLeadingTrivia()
        .Where(t => !t.IsKind(SyntaxKind.WhitespaceTrivia) && !t.IsKind(SyntaxKind.EndOfLineTrivia))
        .ToList();

      var preservedTrailing = member.GetTrailingTrivia()
        .Where(t => !t.IsKind(SyntaxKind.WhitespaceTrivia) && !t.IsKind(SyntaxKind.EndOfLineTrivia))
        .ToList();

      sortedMembers[i] = member
        .WithLeadingTrivia(SyntaxFactory.TriviaList(preservedLeading))
        .WithTrailingTrivia(SyntaxFactory.TriviaList(preservedTrailing));
    }

    // Step 2: Determine which members should have blank lines between them.
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

      bool needsBlankLine = differentGroup || bothMethods;

      int newlinesNeeded = needsBlankLine ? 2 : 1;

      var newTriviaList = new List<SyntaxTrivia>();
      for (int k = 0; k < newlinesNeeded; k++)
      {
        newTriviaList.Add(endOfLineTrivia);
      }
      if (indentation.HasValue)
      {
        newTriviaList.Add(indentation.Value);
      }

      // Add preserved non-whitespace trivia (comments, attributes, etc.)
      newTriviaList.AddRange(currMember.GetLeadingTrivia());

      sortedMembers[i] = currMember.WithLeadingTrivia(SyntaxFactory.TriviaList(newTriviaList));
    }

    // Step 3: Set leading trivia on the first member (preserve original indentation)
    var firstSorted = sortedMembers[0];
    var firstPreserved = firstSorted.GetLeadingTrivia()
      .Where(t => !t.IsKind(SyntaxKind.WhitespaceTrivia) && !t.IsKind(SyntaxKind.EndOfLineTrivia))
      .ToList();

    var firstTrivia = new List<SyntaxTrivia>();
    if (indentation.HasValue)
    {
      firstTrivia.Add(indentation.Value);
    }
    firstTrivia.AddRange(firstPreserved);
    sortedMembers[0] = firstSorted.WithLeadingTrivia(SyntaxFactory.TriviaList(firstTrivia));

    // Step 4: Set trailing trivia on the last member (just a final newline + preserved trailing)
    var lastMember = sortedMembers[sortedMembers.Count - 1];
    var preservedLastTrailing = lastMember.GetTrailingTrivia()
      .Where(t => !t.IsKind(SyntaxKind.WhitespaceTrivia) && !t.IsKind(SyntaxKind.EndOfLineTrivia))
      .ToList();
    var lastTrailing = new List<SyntaxTrivia>(preservedLastTrailing)
    {
      endOfLineTrivia
    };
    sortedMembers[sortedMembers.Count - 1] = lastMember.WithTrailingTrivia(SyntaxFactory.TriviaList(lastTrailing));
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