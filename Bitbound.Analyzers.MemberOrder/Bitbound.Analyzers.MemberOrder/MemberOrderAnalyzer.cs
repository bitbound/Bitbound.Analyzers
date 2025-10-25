using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Bitbound.Analyzers.MemberOrder;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MemberOrderAnalyzer : DiagnosticAnalyzer
{
  public const string DiagnosticId = "BB0001";

  private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AnalyzerTitle), Resources.ResourceManager, typeof(Resources));
  private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.AnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
  private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.AnalyzerDescription), Resources.ResourceManager, typeof(Resources));
  private const string Category = "Layout";

  private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

  public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

  public override void Initialize(AnalysisContext context)
  {
    context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
    context.EnableConcurrentExecution();

    context.RegisterSyntaxNodeAction(AnalyzeTypeDeclaration, SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration, SyntaxKind.InterfaceDeclaration);
  }

  private void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
  {
    var typeDeclaration = (TypeDeclarationSyntax)context.Node;
    var members = typeDeclaration.Members;

    if (members.Count < 2)
    {
      return;
    }

    var memberOrders = members
        .Select(member => (Member: member, Order: GetMemberOrder(member)))
        .ToList();

    var orderedMembers = memberOrders
      .OrderBy(m => m.Order.MemberType)
      .ThenBy(m => m.Order.Accessibility)
      .ThenBy(m => m.Order.StaticInstance)
      // Tie-break: sort alphabetically by identifier when the other bits are equal
      .ThenBy(m => GetIdentifier(m.Member).ValueText, StringComparer.Ordinal)
      .Select(m => m.Member)
      .ToList();

    var targetOrder = new Dictionary<MemberDeclarationSyntax, int>();
    for (int i = 0; i < orderedMembers.Count; i++)
    {
      targetOrder[orderedMembers[i]] = i;
    }

    for (int i = 0; i < members.Count; i++)
    {
      var currentMember = members[i];
      var targetIndex = targetOrder[currentMember];

      if (targetIndex < i)
      {
        var diagnostic = Diagnostic.Create(
            Rule,
            GetIdentifier(currentMember).GetLocation(),
            GetMemberTypeForMessage(currentMember));
        context.ReportDiagnostic(diagnostic);
        // Only report the first one found to avoid spamming.
        // The code fix will sort the whole type anyway.
        return;
      }
    }
  }

  private string GetMemberTypeForMessage(MemberDeclarationSyntax member)
  {
    return member.Kind() switch
    {
      SyntaxKind.FieldDeclaration => "Field",
      SyntaxKind.ConstructorDeclaration => "Constructor",
      SyntaxKind.DestructorDeclaration => "Destructor",
      SyntaxKind.DelegateDeclaration => "Delegate",
      SyntaxKind.EventDeclaration => "Event",
      SyntaxKind.EventFieldDeclaration => "Event",
      SyntaxKind.EnumDeclaration => "Enum",
      SyntaxKind.InterfaceDeclaration => "Interface",
      SyntaxKind.PropertyDeclaration => "Property",
      SyntaxKind.IndexerDeclaration => "Indexer",
      SyntaxKind.MethodDeclaration => "Method",
      SyntaxKind.OperatorDeclaration => "Operator",
      SyntaxKind.ConversionOperatorDeclaration => "Conversion operator",
      SyntaxKind.StructDeclaration => "Struct",
      SyntaxKind.RecordDeclaration => "Record",
      SyntaxKind.RecordStructDeclaration => "Record struct",
      SyntaxKind.ClassDeclaration => "Class",
      _ => member.Kind().ToString(),
    };
  }

  public static SyntaxToken GetIdentifier(MemberDeclarationSyntax member)
  {
    switch (member)
    {
      case PropertyDeclarationSyntax p: return p.Identifier;
      case MethodDeclarationSyntax m: return m.Identifier;
      case FieldDeclarationSyntax f: return f.Declaration.Variables.First().Identifier;
      case ConstructorDeclarationSyntax c: return c.Identifier;
      case EventDeclarationSyntax e: return e.Identifier;
      case IndexerDeclarationSyntax i: return i.ThisKeyword;
      case InterfaceDeclarationSyntax i: return i.Identifier;
      case StructDeclarationSyntax s: return s.Identifier;
      case OperatorDeclarationSyntax o: return o.OperatorToken;
      case ConversionOperatorDeclarationSyntax co: return co.Type.GetFirstToken();
      case RecordDeclarationSyntax r: return r.Identifier;
      case EnumDeclarationSyntax e: return e.Identifier;
      case ClassDeclarationSyntax c: return c.Identifier;
      default: return member.GetFirstToken();
    }
  }

  public static (int MemberType, int Accessibility, int StaticInstance) GetMemberOrder(MemberDeclarationSyntax member)
  {
    var memberType = GetMemberTypeOrder(member);
    var accessibility = GetAccessibilityOrder(member);
    var staticInstance = GetStaticInstanceOrder(member);

    return (memberType, accessibility, staticInstance);
  }

  public static int GetMemberTypeOrder(MemberDeclarationSyntax member)
  {
    switch (member.Kind())
    {
      // Fields
      case SyntaxKind.FieldDeclaration:
        var modifiers = member.Modifiers;
        if (modifiers.Any(SyntaxKind.ConstKeyword)) return 1;
        bool isStatic = modifiers.Any(SyntaxKind.StaticKeyword);
        bool isReadOnly = modifiers.Any(SyntaxKind.ReadOnlyKeyword);
        if (isStatic && isReadOnly) return 2;
        if (isStatic) return 3;
        if (isReadOnly) return 4;
        return 5;

      // Constructors
      case SyntaxKind.ConstructorDeclaration when member.Modifiers.Any(SyntaxKind.StaticKeyword):
        return 6;
      case SyntaxKind.ConstructorDeclaration:
        return 7;

      // Destructor
      case SyntaxKind.DestructorDeclaration:
        return 8;

      // Delegates
      case SyntaxKind.DelegateDeclaration:
        return 9;

      // Events
      case SyntaxKind.EventDeclaration:
      case SyntaxKind.EventFieldDeclaration:
        return 10;

      // Properties
      case SyntaxKind.PropertyDeclaration:
        return 11;

      // Indexers
      case SyntaxKind.IndexerDeclaration:
        return 12;

      // Methods
      case SyntaxKind.MethodDeclaration:
        return 13;

      // Operators
      case SyntaxKind.ConversionOperatorDeclaration:
        return 14;
      case SyntaxKind.OperatorDeclaration:
        return 15;

      // Nested types
      case SyntaxKind.EnumDeclaration:
        return 16;
      case SyntaxKind.InterfaceDeclaration:
        return 17;
      case SyntaxKind.StructDeclaration:
        return 18;
      case SyntaxKind.RecordStructDeclaration:
        return 19;
      case SyntaxKind.RecordDeclaration:
        return 20;
      case SyntaxKind.ClassDeclaration:
        return 21;

      default:
        return 100;
    }
  }

  public static int GetAccessibilityOrder(MemberDeclarationSyntax member)
  {
    var modifiers = member.Modifiers;

    if (modifiers.Any(SyntaxKind.PublicKeyword)) return 1;

    bool isProtected = modifiers.Any(SyntaxKind.ProtectedKeyword);
    bool isInternal = modifiers.Any(SyntaxKind.InternalKeyword);

    if (isProtected && isInternal) return 3; // protected internal
    if (isInternal) return 2; // internal

    bool isPrivate = modifiers.Any(SyntaxKind.PrivateKeyword);

    if (isProtected && isPrivate) return 5; // private protected
    if (isProtected) return 4; // protected

    if (isPrivate) return 6; // private

    // Default accessibility
    if (member.Parent is InterfaceDeclarationSyntax)
    {
      return 1; // public
    }

    return 6; // private for class/struct
  }

  public static int GetStaticInstanceOrder(MemberDeclarationSyntax member)
  {
    // static members first, then instance
    return member.Modifiers.Any(SyntaxKind.StaticKeyword) ? 1 : 2;
  }
}