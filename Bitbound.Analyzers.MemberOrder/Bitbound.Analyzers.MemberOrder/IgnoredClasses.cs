namespace Bitbound.Analyzers.MemberOrder;

public static class IgnoredClasses
{
  public static readonly IgnoredClass[] Classes =
  [
    new IgnoredClass("Microsoft.EntityFrameworkCore.Migrations", "Migration"),
  ];
}

public class IgnoredClass(string @namespace, string name)
{
  public string FullyQualifiedName => $"{Namespace}.{Name}";
  public string Name { get; } = name;
  public string Namespace { get; } = @namespace;
}