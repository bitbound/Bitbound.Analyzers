using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VerifyCS = Bitbound.Analyzers.MemberOrder.Test.CSharpCodeFixVerifier<
  Bitbound.Analyzers.MemberOrder.MemberOrderAnalyzer,
  Bitbound.Analyzers.MemberOrder.MemberOrderCodeFixProvider>;

namespace Bitbound.Analyzers.MemberOrder.Test;

[TestClass]
public class MemberOrderSpacingTests
{
  [TestMethod]
  public async Task Fix_InsertsSingleBlankLine_BetweenGroups()
  {
    var test = """
            namespace MyCode
            {
                public class MyClass
                {
                    public void Method() { }
                    public int {|#0:Field|};
                }
            }
            """;

    var expectedSource = """
            namespace MyCode
            {
                public class MyClass
                {
                    public int Field;

                    public void Method() { }
                }
            }
            """;

    var expectedDiagnostic = VerifyCS.Diagnostic("BB0001").WithLocation(0).WithArguments("Field");
    await VerifyCS.VerifyCodeFixAsync(test, expectedDiagnostic, expectedSource);
  }

  [TestMethod]
  public async Task Fix_ReducesExcessiveBlankLines_BetweenGroups()
  {
    var test = """
            namespace MyCode
            {
                public class MyClass
                {
                    public int Field1;


                    public void Method() { }

                    public int {|#0:Field2|};
                }
            }
            """;

    // Field1 and Field2 are fields. Method is method.
    // Sorted: Field1, Field2, Method.
    // Field2 moves to be after Field1. Same group.
    // Method moves to be after Field2. Different group.
    // Method originally had excessive blank lines.

    var expectedSource = """
            namespace MyCode
            {
                public class MyClass
                {
                    public int Field1;
                    public int Field2;

                    public void Method() { }
                }
            }
            """;

    var expectedDiagnostic = VerifyCS.Diagnostic("BB0001").WithLocation(0).WithArguments("Field2");
    await VerifyCS.VerifyCodeFixAsync(test, expectedDiagnostic, expectedSource);
  }

  [TestMethod]
  public async Task Fix_InterleavedGroups_GroupsAreTightAndSeparated()
  {
    var test = """
            namespace MyCode
            {
                public class MyClass
                {
                    public void MethodA() { }

                    public int FieldA;



                    public void MethodB() { }



                    public int FieldB;
                }
            }
            """;

    // Expected behavior:
    // 1. Fields move to top.
    // 2. Methods move to bottom.
    // 3. FieldA and FieldB are same group -> Tight spacing (no blank line).
    // 4. MethodA and MethodB are same group -> Tight spacing (no blank line).
    // 5. Gap between FieldB and MethodA -> Blank line.

    var expectedSource = """
            namespace MyCode
            {
                public class MyClass
                {
                    public int FieldA;
                    public int FieldB;

                    public void MethodA() { }
                    public void MethodB() { }
                }
            }
            """;
    _ = VerifyCS.Diagnostic("BB0001").WithLocation(0).WithArguments("FieldA");
    _ = VerifyCS.Diagnostic("BB0001").WithLocation(0).WithArguments("FieldB");

    var expectedDiagnostics = new[] {
            VerifyCS.Diagnostic("BB0001").WithSpan(7, 20, 7, 26).WithArguments("FieldA")
        };

    await VerifyCS.VerifyCodeFixAsync(test, expectedDiagnostics, expectedSource);
  }

  [TestMethod]
  public async Task Fix_PropertiesWithAttributes_NoExcessiveWhitespace()
  {
    var test = """
            namespace MyCode
            {
                public interface IProcess
                {
                    int BasePriority { get; }
                    bool EnableRaisingEvents { get; set; }
                    [System.Obsolete]
                    nint ProcessorAffinity { get; set; }

                    string StandardInput { get; }
                    string StandardOutput { get; }

                    bool {|#0:Responding|} { get; }
                    int SessionId { get; }
                    string StartInfo { get; set; }

                    void Kill();
                }
            }
            """;

    var expectedSource = """
            namespace MyCode
            {
                public interface IProcess
                {
                    int BasePriority { get; }
                    bool EnableRaisingEvents { get; set; }
                    [System.Obsolete]
                    nint ProcessorAffinity { get; set; }
                    bool Responding { get; }
                    int SessionId { get; }
                    string StandardInput { get; }
                    string StandardOutput { get; }
                    string StartInfo { get; set; }

                    void Kill();
                }
            }
            """;

    var expectedDiagnostic = VerifyCS.Diagnostic("BB0001").WithLocation(0).WithArguments("Responding");
    await VerifyCS.VerifyCodeFixAsync(test, expectedDiagnostic, expectedSource);
  }
}
