using Microsoft.VisualStudio.TestTools.UnitTesting;
using VerifyCS = Bitbound.Analyzers.MemberOrder.Test.CSharpCodeFixVerifier<
  Bitbound.Analyzers.MemberOrder.MemberOrderAnalyzer,
  Bitbound.Analyzers.MemberOrder.MemberOrderCodeFixProvider>;

namespace Bitbound.Analyzers.MemberOrder.Test;

[TestClass]
public class MemberOrderUnitTests
{

  [TestMethod]
  public async Task CorrectOrder_NoDiagnostic()
  {
    var test = """
      namespace MyCode
      {
        public class MyClass
        {
          public const int ConstField = 1;

          public static readonly int StaticReadOnlyField = 2;

          public static int StaticField = 3;

          protected readonly int ReadOnlyField = 4;

          private int _field = 5;

          static MyClass()
          {
          }

          public MyClass()
          {
          }

          ~MyClass()
          {
          }

          public delegate void MyDelegate();

          public event System.EventHandler MyEvent;

          public static int StaticProperty { get; set; }

          public int InstanceProperty { get; set; }

          public int this[int index]
          {
            get => 0;
            set { }
          }

          public static void StaticMethod()
          {
          }

          public void PublicMethod()
          {
          }

          public static explicit operator int(MyClass value) => 0;

          public static MyClass operator +(MyClass a, MyClass b) => a;

          public enum InnerEnum
          {
            One,
            Two
          }

          public interface IInnerInterface
          {
          }

          public struct InnerStruct
          {
          }

          public record InnerRecord
          {
          }
          
          public class InnerClass
          {
          }
        }
      }
      """;

    await VerifyCS.VerifyAnalyzerAsync(test);
  }

  [TestMethod]
  public async Task MethodBeforeProperty_DiagnosticAndFix()
  {
    var test = """
      namespace MyCode
      {
        public class MyClass
        {
          public void MyMethod() { }
          public int {|#0:MyProperty|} { get; set; }
        }
      }
      """;

    var fixtest = """
      namespace MyCode
      {
        public class MyClass
        {
          public int MyProperty { get; set; }

          public void MyMethod() { }
        }
      }
      """;

    var expected = VerifyCS.Diagnostic("BB0001").WithLocation(0).WithArguments("Property");
    await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
  }

  [TestMethod]
  public async Task FieldKindOutOfOrder_DiagnosticAndFix()
  {
    var test = """
      namespace MyCode
      {
        public class MyClass
        {
          private int _regularField;
          private const int {|#0:ConstField|} = 1;
        }
      }
      """;

    var fixtest = """
      namespace MyCode
      {
        public class MyClass
        {
          private const int ConstField = 1;

          private int _regularField;
        }
      }
      """;

    var expected = VerifyCS.Diagnostic("BB0001").WithLocation(0).WithArguments("Field");
    await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
  }

  [TestMethod]
  public async Task AccessibilityOutOfOrder_DiagnosticAndFix()
  {
    var test = """
      namespace MyCode
      {
        public class MyClass
        {
          private int PrivateProperty { get; set; }
          public int {|#0:PublicProperty|} { get; set; }
        }
      }
      """;

    var fixtest = """
      namespace MyCode
      {
        public class MyClass
        {
          public int PublicProperty { get; set; }

          private int PrivateProperty { get; set; }
        }
      }
      """;

    var expected = VerifyCS.Diagnostic("BB0001").WithLocation(0).WithArguments("Property");
    await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
  }

  [TestMethod]
  public async Task StaticBeforeInstance_DiagnosticAndFix()
  {
    var test = """
      namespace MyCode
      {
        public class MyClass
        {
          public void InstanceMethod() { }
          public static void {|#0:StaticMethod|}() { }
        }
      }
      """;

    var fixtest = """
      namespace MyCode
      {
        public class MyClass
        {
          public static void StaticMethod() { }

          public void InstanceMethod() { }
        }
      }
      """;

    var expected = VerifyCS.Diagnostic("BB0001").WithLocation(0).WithArguments("Method");
    await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
  }

   [TestMethod]
  public async Task NestedTypesOrder_DiagnosticAndFix()
  {
    var test = """
      namespace MyCode
      {
        public class MyClass
        {
          public record InnerRecord { }
          public class InnerClass { }
          public enum {|#0:InnerEnum|} { One, Two }
          public interface IInnerInterface { }
          public struct InnerStruct { }
        }
      }
      """;

    var fixtest = """
      namespace MyCode
      {
        public class MyClass
        {
          public enum InnerEnum { One, Two }

          public interface IInnerInterface { }

          public struct InnerStruct { }

          public record InnerRecord { }

          public class InnerClass { }
        }
      }
      """;

    var expected = VerifyCS.Diagnostic("BB0001").WithLocation(0).WithArguments("Enum");
    await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
  }


  [TestMethod]
  public async Task ConversionOperatorBeforeOperator_DiagnosticAndFix()
  {
    var test = """
      namespace MyCode
      {
        public class MyClass
        {
          public static MyClass operator +(MyClass a, MyClass b) => a;
          public static explicit operator {|#0:int|}(MyClass value) => 0;
        }
      }
      """;

    var fixtest = """
      namespace MyCode
      {
        public class MyClass
        {
          public static explicit operator int(MyClass value) => 0;

          public static MyClass operator +(MyClass a, MyClass b) => a;
        }
      }
      """;

    var expected = VerifyCS.Diagnostic("BB0001").WithLocation(0).WithArguments("Conversion operator");
    await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
  }

  [TestMethod]
  public async Task ProtectedInternalVsProtected_DiagnosticAndFix()
  {
    var test = """
      namespace MyCode
      {
        public class MyClass
        {
          protected int ProtProp { get; set; }
          protected internal int {|#0:ProtIntProp|} { get; set; }
        }
      }
      """;

    var fixtest = """
      namespace MyCode
      {
        public class MyClass
        {
          protected internal int ProtIntProp { get; set; }

          protected int ProtProp { get; set; }
        }
      }
      """;

    var expected = VerifyCS.Diagnostic("BB0001").WithLocation(0).WithArguments("Property");
    await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
  }

  [TestMethod]
  public async Task FullFieldOrder_DiagnosticAndFix()
  {
    var test = """
      namespace MyCode
      {
        public class MyClass
        {
          private int _instanceField;
          public static readonly int StaticReadonlyField = 1;
          public const int {|#0:ConstField|} = 0;
          public const int AnotherConstField = 3;
          private readonly int _readonlyField;
          public static int StaticField = 2;
        }
      }
      """;

    var fixtest = """
      namespace MyCode
      {
        public class MyClass
        {
          public const int ConstField = 0;
          public const int AnotherConstField = 3;

          public static readonly int StaticReadonlyField = 1;

          public static int StaticField = 2;

          private readonly int _readonlyField;

          private int _instanceField;
        }
      }
      """;

    var expected = VerifyCS.Diagnostic("BB0001").WithLocation(0).WithArguments("Field");
    await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
  }
}