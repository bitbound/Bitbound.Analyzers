using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Bitbound.Analyzers.MemberOrder;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MemberOrderAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "Layout";
    public const string DiagnosticId = "BB0001";

    private static readonly LocalizableString Description =
        new LocalizableResourceString(nameof(Resources.AnalyzerDescription), Resources.ResourceManager,
            typeof(Resources));

    private static readonly LocalizableString MessageFormat =
        new LocalizableResourceString(nameof(Resources.AnalyzerMessageFormat), Resources.ResourceManager,
            typeof(Resources));

    private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AnalyzerTitle),
        Resources.ResourceManager, typeof(Resources));

    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, Category,
        DiagnosticSeverity.Warning, true, Description);


    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public static SyntaxToken GetIdentifier(MemberDeclarationSyntax member)
    {
        return member switch
        {
            PropertyDeclarationSyntax p => p.Identifier,
            MethodDeclarationSyntax m => m.Identifier,
            FieldDeclarationSyntax f => f.Declaration.Variables.First().Identifier,
            ConstructorDeclarationSyntax c => c.Identifier,
            EventDeclarationSyntax e => e.Identifier,
            IndexerDeclarationSyntax i => i.ThisKeyword,
            InterfaceDeclarationSyntax i => i.Identifier,
            StructDeclarationSyntax s => s.Identifier,
            OperatorDeclarationSyntax o => o.OperatorToken,
            ConversionOperatorDeclarationSyntax co => co.Type.GetFirstToken(),
            RecordDeclarationSyntax r => r.Identifier,
            EnumDeclarationSyntax e => e.Identifier,
            ClassDeclarationSyntax c => c.Identifier,
            DelegateDeclarationSyntax d => d.Identifier,
            _ => member.GetFirstToken()
        };
    }

    public static (int MemberType, int Accessibility, int ExternOrder, int StaticInstance) GetMemberOrder(MemberDeclarationSyntax member)
    {
        var memberType = GetMemberTypeOrder(member);
        var accessibility = GetAccessibilityOrder(member);
        var externOrder = GetExternOrder(member);
        var staticInstance = GetStaticInstanceOrder(member);

        return (memberType, accessibility, externOrder, staticInstance);
    }

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeTypeDeclaration, SyntaxKind.ClassDeclaration,
            SyntaxKind.StructDeclaration, SyntaxKind.InterfaceDeclaration);
    }

    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
    {
        var typeDeclaration = (TypeDeclarationSyntax)context.Node;
        var members = typeDeclaration.Members;

        if (members.Count < 2) return;

        if (IsEfCoreMigration(typeDeclaration, context.SemanticModel)) return;

        if (IsStructWithStrictLayout(typeDeclaration, context.SemanticModel)) return;

        var memberOrders = members
            .Select(member => (Member: member, Order: GetMemberOrder(member)))
            .ToList();

        var orderedMembers = memberOrders
            .OrderBy(m => m.Order.MemberType)
            .ThenBy(m => m.Order.Accessibility)
            .ThenBy(m => m.Order.ExternOrder)
            .ThenBy(m => m.Order.StaticInstance)
            // Tie-break: sort alphabetically by identifier, ignoring case
            .ThenBy(m => GetIdentifier(m.Member).ValueText, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => GetIdentifier(m.Member).ValueText, StringComparer.Ordinal)
            .Select(m => m.Member)
            .ToList();

        var targetOrder = new Dictionary<MemberDeclarationSyntax, int>();
        for (var i = 0; i < orderedMembers.Count; i++) targetOrder[orderedMembers[i]] = i;

        for (var i = 1; i < members.Count; i++)
        {
            var currentMember = members[i];
            var previousMember = members[i - 1];
            var currentTarget = targetOrder[currentMember];
            var previousTarget = targetOrder[previousMember];

            if (currentTarget >= previousTarget) continue;

            var diagnostic = Diagnostic.Create(
                Rule,
                GetIdentifier(currentMember).GetLocation(),
                GetIdentifier(currentMember).ValueText);

            context.ReportDiagnostic(diagnostic);
            // Only report the first one found to avoid spamming.
            // The code fix will sort the whole type anyway.
            return;
        }
    }

    private static int GetAccessibilityOrder(MemberDeclarationSyntax member)
    {
        var modifiers = member.Modifiers;

        if (modifiers.Any(SyntaxKind.PublicKeyword)) return 1;

        var isProtected = modifiers.Any(SyntaxKind.ProtectedKeyword);
        var isInternal = modifiers.Any(SyntaxKind.InternalKeyword);

        if (isProtected && isInternal) return 3; // protected internal
        if (isInternal) return 2; // internal

        var isPrivate = modifiers.Any(SyntaxKind.PrivateKeyword);

        if (isProtected && isPrivate) return 5; // private-protected
        if (isProtected) return 4; // protected

        if (isPrivate) return 6; // private

        // Default accessibility
        if (member.Parent is InterfaceDeclarationSyntax) return 1; // public

        return 6; // private for class/struct
    }

    private static int GetMemberTypeOrder(MemberDeclarationSyntax member)
    {
        switch (member.Kind())
        {
            // Fields
            case SyntaxKind.FieldDeclaration:
                var modifiers = member.Modifiers;
                if (modifiers.Any(SyntaxKind.ConstKeyword)) return 1;
                var isStatic = modifiers.Any(SyntaxKind.StaticKeyword);
                var isReadOnly = modifiers.Any(SyntaxKind.ReadOnlyKeyword);
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

    private static int GetStaticInstanceOrder(MemberDeclarationSyntax member)
    {
        // static members first, then instance
        return member.Modifiers.Any(SyntaxKind.StaticKeyword) ? 1 : 2;
    }

    private static int GetExternOrder(MemberDeclarationSyntax member)
    {
        // Check if member has DllImport or LibraryImport attribute (P/Invoke methods)
        var hasPInvokeAttribute = member.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(attr =>
            {
                var name = attr.Name.ToString();
                return name.Contains("DllImport") || name.Contains("LibraryImport");
            });

        // Only P/Invoke methods go last, not all extern members
        return hasPInvokeAttribute ? 2 : 1;
    }

    private static bool IsEfCoreMigration(TypeDeclarationSyntax typeDeclaration, SemanticModel contextSemanticModel)
    {
        var typeSymbol = contextSemanticModel.GetDeclaredSymbol(typeDeclaration);
        return typeSymbol?.BaseType is { } baseType &&
               baseType.ToDisplayString() == "Microsoft.EntityFrameworkCore.Migrations.Migration";
    }

    private static bool IsStructWithStrictLayout(TypeDeclarationSyntax typeDeclaration, SemanticModel semanticModel)
    {
        if (typeDeclaration is not StructDeclarationSyntax) return false;

        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration);
        if (typeSymbol is null) return false;

        foreach (var attribute in typeSymbol.GetAttributes())
            if (attribute.AttributeClass?.Name == nameof(StructLayoutAttribute) &&
                attribute.ConstructorArguments.Length > 0 &&
                attribute.ConstructorArguments[0].Value is int layoutKind &&
                layoutKind is (int)LayoutKind.Sequential or (int)LayoutKind.Explicit)
                return true;

        return false;
    }
}