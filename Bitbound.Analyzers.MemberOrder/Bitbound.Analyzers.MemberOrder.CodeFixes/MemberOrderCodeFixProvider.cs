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

    // Add blank lines between member type groups
    for (int i = 1; i < sortedMembers.Count; i++)
    {
      var prevOrder = MemberOrderAnalyzer.GetMemberOrder(sortedMembers[i - 1]);
      var currentOrder = MemberOrderAnalyzer.GetMemberOrder(sortedMembers[i]);

      if (prevOrder.MemberType != currentOrder.MemberType ||
          prevOrder.Accessibility != currentOrder.Accessibility ||
          prevOrder.StaticInstance != currentOrder.StaticInstance)
      {
        var existingTrivia = sortedMembers[i].GetLeadingTrivia();
        var newTrivia = SyntaxFactory
            .TriviaList(SyntaxFactory.CarriageReturnLineFeed)
            .AddRange(existingTrivia);

        sortedMembers[i] = sortedMembers[i].WithLeadingTrivia(newTrivia);
      }
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