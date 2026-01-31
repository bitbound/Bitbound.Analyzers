# Member Ordering Analyzer - Rules and Implementation Guide

## Purpose
This analyzer enforces consistent member ordering within C# types (classes, structs, interfaces) and applies deterministic spacing rules.

## Member Ordering Rules

Members are sorted in the following order:

### 1. Member Type Priority
1. **Fields** (in order):
   - `const` fields
   - `static readonly` fields
   - `static` fields
   - `readonly` fields (instance)
   - Regular fields (instance)
2. **Constructors**:
   - Static constructors
   - Instance constructors
3. **Destructors**
4. **Delegates**
5. **Events**
6. **Properties**
7. **Indexers**
8. **Methods**
9. **Operators**:
   - Conversion operators
   - Operator overloads
10. **Nested Types** (in order):
    - Enums
    - Interfaces
    - Structs
    - Record structs
    - Records
    - Classes

### 2. Accessibility Priority (within each member type)
1. `public`
2. `internal`
3. `protected internal`
4. `protected`
5. `private protected`
6. `private`

### 3. Static vs Instance
- Static members before instance members (within same member type and accessibility)

### 4. P/Invoke Methods (DllImport/LibraryImport)
- P/Invoke methods go LAST within their group (after regular methods with same modifiers)

### 5. Alphabetical Sorting
- Within the same category (same member type, accessibility, static/instance, extern), members are sorted alphabetically by name

## Spacing Rules

### CRITICAL: No Spacing Preservation
**Existing spacing is NEVER preserved.** Spacing is always applied deterministically based on the rules below.

### Rule 1: Between Different Groups
- **ONE blank line** between different member groups
- Groups are defined by: MemberType + Accessibility + StaticInstance + ExternOrder

### Rule 2: Within Same Group (Non-Methods)
- **NO blank lines** between members of the same group
- Applies to: fields, properties, events, delegates, indexers, operators, nested types

### Rule 3: Methods in Classes
- **ONE blank line** between methods in classes (even with same modifiers)
- This applies to all methods with the same group characteristics

### Rule 4: Methods in Interfaces  
- **NO blank lines** between methods in interfaces
- Interface methods are tightly packed regardless of modifiers

### Rule 5: Nested Types
- Private nested types (classes, structs, interfaces, records, etc.) are placed at the bottom
- Follow same ordering rules within nested types

## Exclusions

The analyzer does NOT enforce ordering for:

1. **EF Core Migrations** - Classes inheriting from `Microsoft.EntityFrameworkCore.Migrations.Migration`
2. **Structs with Explicit Layout** - Structs marked with `[StructLayout(LayoutKind.Sequential)]` or `[StructLayout(LayoutKind.Explicit)]`
3. **Enums** - Enum members maintain their declaration order

## Implementation Details

### Key Methods

#### `GetMemberOrder(MemberDeclarationSyntax member)`
Returns a tuple: `(MemberType, Accessibility, ExternOrder, StaticInstance)`
- Used for sorting and grouping members

#### `GetIdentifier(MemberDeclarationSyntax member)`
Gets the identifier token for a member (handles all member types including indexers, operators)

#### `CopyWhiteSpace()`
Applies spacing rules deterministically:
1. Normalizes trailing trivia (removes excessive newlines)
2. Determines spacing based on:
   - Whether groups are different (different group = 1 blank line)
   - Whether both members are methods in a class (methods in class = 1 blank line)
   - Otherwise no blank lines within same group
3. Preserves comments and attributes
4. Maintains proper indentation

### Spacing Logic Flow

```csharp
if (differentGroup)
{
    // Add 1 blank line between different groups
    newlinesNeeded = prevHasNewline ? 1 : 2;
}
else if (bothMethods && !isInterface)
{
    // Add 1 blank line between methods in classes
    newlinesNeeded = prevHasNewline ? 1 : 2;
}
else
{
    // No blank lines within same group (tight spacing)
    newlinesNeeded = prevHasNewline ? 0 : 1;
}
```

## Testing Philosophy

- **Tests define the expected behavior** - Never modify existing tests without explicit permission
- Each test validates a specific spacing or ordering scenario
- Test expectations are always correct; implementation must match them

## StyleCop Alignment

This analyzer follows StyleCop ordering rules (SA1201-SA1204) with the specific spacing rules defined above.

## Future Modifications

When modifying this analyzer:
1. **Read existing tests first** to understand expected behavior
2. **Never change tests** without explicit permission
3. **Ask for clarification** if requirements are unclear or conflicting
4. Run full test suite after any changes
5. Spacing rules are deterministic - do not add "preservation" logic
