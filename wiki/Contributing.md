# Contributing to Quark

Thank you for your interest in contributing to Quark! This guide will help you get started.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [How Can I Contribute?](#how-can-i-contribute)
- [Development Setup](#development-setup)
- [Building and Testing](#building-and-testing)
- [Code Style and Conventions](#code-style-and-conventions)
- [Submitting Changes](#submitting-changes)
- [Issue Reporting](#issue-reporting)
- [Documentation](#documentation)
- [Community](#community)

---

## Code of Conduct

### Our Pledge

We are committed to providing a welcoming and inclusive environment for everyone. We expect all contributors to:

- Be respectful and considerate
- Accept constructive criticism gracefully
- Focus on what's best for the community
- Show empathy towards other community members

### Unacceptable Behavior

- Harassment, discrimination, or offensive comments
- Trolling, insulting, or derogatory remarks
- Publishing others' private information
- Any conduct that would be inappropriate in a professional setting

### Enforcement

Instances of unacceptable behavior may be reported to the project maintainers. All complaints will be reviewed and investigated, resulting in a response deemed necessary and appropriate.

---

## How Can I Contribute?

### Reporting Bugs

Found a bug? Please help us by:

1. **Search existing issues** - Your bug might already be reported
2. **Create a new issue** - If not found, open a [new issue](https://github.com/thnak/Quark/issues/new)
3. **Provide details** - Include reproduction steps, expected vs actual behavior, environment info

### Suggesting Features

Have an idea? We'd love to hear it:

1. **Check roadmap** - Review [PROGRESS.md](../docs/PROGRESS.md) to see if it's already planned
2. **Open a discussion** - Start a [discussion](https://github.com/thnak/Quark/discussions) to gauge interest
3. **Create an issue** - If there's community support, create a feature request issue

### Contributing Code

Ready to code? Here's how:

1. **Find an issue** - Look for issues labeled `good first issue` or `help wanted`
2. **Comment on the issue** - Let others know you're working on it
3. **Fork and code** - Fork the repo, create a branch, implement your changes
4. **Submit a PR** - Create a pull request with a clear description

### Improving Documentation

Documentation is crucial! You can help by:

- Fixing typos or unclear explanations
- Adding examples or tutorials
- Expanding API documentation
- Translating documentation (future)

---

## Development Setup

### Prerequisites

Before you begin, ensure you have:

- **.NET 10 SDK** or later ([Download](https://dotnet.microsoft.com/download))
- **Git** for version control
- **IDE**: Visual Studio 2022, VS Code with C# extension, or JetBrains Rider
- **Docker** (optional, for integration tests)

### Clone the Repository

```bash
# Clone your fork
git clone https://github.com/YOUR_USERNAME/Quark.git
cd Quark

# Add upstream remote
git remote add upstream https://github.com/thnak/Quark.git

# Verify remotes
git remote -v
```

### Install Dependencies

```bash
# Restore NuGet packages
dotnet restore

# Verify everything builds
dotnet build -maxcpucount
```

### Verify Setup

```bash
# Run all tests (requires Docker)
dotnet test

# Run a basic example
dotnet run --project examples/Quark.Examples.Basic
```

If everything works, you're ready to contribute!

---

## Building and Testing

### Project Structure

```
Quark/
├── src/                        # Source code
│   ├── Quark.Abstractions/     # Pure interfaces (no implementation)
│   ├── Quark.Core.*/           # Core implementations
│   ├── Quark.Generators/       # Source generators (netstandard2.0)
│   ├── Quark.Clustering.*/     # Clustering implementations
│   ├── Quark.Storage.*/        # Storage backends
│   └── ...
├── tests/
│   └── Quark.Tests/            # All tests (182 tests)
├── examples/                   # Example projects
└── docs/                       # Documentation
```

### Building

```bash
# Build all projects
dotnet build -maxcpucount

# Build specific project
dotnet build src/Quark.Core.Actors/Quark.Core.Actors.csproj

# Build in Release mode
dotnet build -c Release -maxcpucount

# Clean build
dotnet clean
dotnet build -maxcpucount
```

### Running Tests

```bash
# Run all tests
dotnet test

# Run with verbose output
dotnet test -v normal

# Run specific test
dotnet test --filter "FullyQualifiedName~ActorFactoryTests"

# Run tests without building
dotnet test --no-build

# Generate code coverage (if configured)
dotnet test --collect:"XPlat Code Coverage"
```

### Test Requirements

**Docker Required:** Tests use `Testcontainers.Redis` which requires Docker:

```bash
# Verify Docker is running
docker ps

# If Docker unavailable, some tests will fail
# This is expected - CI will run full test suite
```

### Running Examples

```bash
# Basic example
dotnet run --project examples/Quark.Examples.Basic

# Supervision example
dotnet run --project examples/Quark.Examples.Supervision

# Streaming example
dotnet run --project examples/Quark.Examples.Streaming
```

### Publishing with AOT

```bash
# Publish with Native AOT
dotnet publish -c Release -r linux-x64 --self-contained \
  examples/Quark.Examples.Basic

# Run the native binary
./examples/Quark.Examples.Basic/bin/Release/net10.0/linux-x64/publish/Quark.Examples.Basic
```

---

## Code Style and Conventions

Quark follows consistent coding standards to maintain readability and quality.

### General Principles

1. **Simplicity** - Favor simple, clear code over clever solutions
2. **Consistency** - Match existing code style
3. **AOT-First** - Never use reflection or dynamic code generation
4. **Performance** - Optimize hot paths, but don't sacrifice readability

### File Organization

- **One public type per file**
- File name matches type name: `IActor.cs`, `ActorBase.cs`
- Interfaces start with `I`: `IActor`, `IActorFactory`
- Use namespaces matching folder structure

```csharp
// File: src/Quark.Core.Actors/ActorBase.cs
namespace Quark.Core.Actors;

public abstract class ActorBase : IActor
{
    // Implementation
}
```

### Naming Conventions

```csharp
// Interfaces: I + PascalCase
public interface IActor { }
public interface IActorFactory { }

// Classes: PascalCase
public class ActorBase { }
public class ActorFactory { }

// Methods: PascalCase
public async Task OnActivateAsync(CancellationToken cancellationToken = default)
{
}

// Private fields: _camelCase with underscore
private int _count;
private readonly IActorFactory _factory;

// Properties: PascalCase
public string ActorId { get; }
public int Count { get; set; }

// Parameters: camelCase
public void DoWork(string actorId, int timeout)
{
}

// Constants: PascalCase
private const int DefaultTimeout = 30;
```

### Code Style

```csharp
// ✅ Good: Use nullable reference types
public string? GetName() => _name;

// ✅ Good: Use expression bodies for simple methods
public int GetCount() => _count;

// ✅ Good: Use async/await properly
public async Task<int> GetValueAsync(CancellationToken cancellationToken = default)
{
    await LoadDataAsync(cancellationToken);
    return _value;
}

// ✅ Good: Include XML documentation for public APIs
/// <summary>
/// Creates a new actor instance with the specified ID.
/// </summary>
/// <param name="actorId">The unique identifier for the actor.</param>
/// <returns>The created actor instance.</returns>
public TActor CreateActor<TActor>(string actorId) where TActor : IActor;

// ❌ Bad: Don't use reflection
var type = Type.GetType("MyActor"); // NO!
var actor = Activator.CreateInstance(type); // NO!

// ✅ Good: Use source generation instead
[Actor]
public class MyActor : ActorBase { }
```

### Formatting

- **Indentation**: 4 spaces (no tabs)
- **Line length**: Try to keep under 120 characters
- **Braces**: Open brace on same line (K&R style)
- **Spacing**: Space after keywords, before opening braces

```csharp
// ✅ Good formatting
public async Task ProcessAsync(CancellationToken cancellationToken = default)
{
    if (IsReady())
    {
        await DoWorkAsync(cancellationToken);
    }
    else
    {
        throw new InvalidOperationException("Not ready");
    }
}
```

### Comments

- **XML docs** for all public APIs
- **Inline comments** only when code needs clarification
- **Don't** comment obvious code
- **Do** explain "why" not "what"

```csharp
// ❌ Bad: States the obvious
// Increment the counter
_count++;

// ✅ Good: Explains non-obvious logic
// Use consistent hashing to ensure same actor ID always maps to same silo
var siloIndex = HashActorId(actorId) % activeSilos.Count;

// ✅ Good: Explains workaround
// WORKAROUND: Redis Lua script doesn't support ZADD GT option in Redis 6.0
// We fetch-compare-update instead (fixed in Redis 6.2+)
```

### Error Handling

```csharp
// ✅ Good: Use specific exceptions
throw new ArgumentNullException(nameof(actorId));
throw new InvalidOperationException("Actor not activated");

// ✅ Good: Add context to exceptions
catch (RedisException ex)
{
    throw new StateStorageException($"Failed to load state for {actorId}", ex);
}

// ❌ Bad: Swallow exceptions silently
catch (Exception) { } // NO!

// ✅ Good: Log before throwing
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to activate actor {ActorId}", actorId);
    throw;
}
```

### Testing Conventions

```csharp
// Test class naming: {ClassUnderTest}Tests
public class ActorFactoryTests
{
    // Test method naming: {Method}_{Scenario}_{ExpectedResult}
    [Fact]
    public void CreateActor_WithValidId_ReturnsActor()
    {
        // Arrange
        var factory = new ActorFactory();

        // Act
        var actor = factory.CreateActor<TestActor>("test-1");

        // Assert
        Assert.NotNull(actor);
        Assert.Equal("test-1", actor.ActorId);
    }

    // Use async tests for async code
    [Fact]
    public async Task OnActivateAsync_FirstTime_InitializesState()
    {
        // Arrange
        var actor = new TestActor("test-1");

        // Act
        await actor.OnActivateAsync();

        // Assert
        Assert.True(actor.IsActivated);
    }
}
```

---

## Submitting Changes

### Branch Naming

Use descriptive branch names:

```bash
# Feature branches
git checkout -b feature/add-timers
git checkout -b feature/postgres-storage

# Bug fixes
git checkout -b fix/actor-factory-race-condition
git checkout -b fix/memory-leak-in-mailbox

# Documentation
git checkout -b docs/update-clustering-guide
git checkout -b docs/fix-typos
```

### Commit Messages

Write clear, concise commit messages:

```
# Good commit messages
feat: Add timer support to ActorBase
fix: Resolve race condition in ActorFactory
docs: Update clustering configuration guide
test: Add tests for supervision directives
refactor: Simplify mailbox implementation

# Include details in commit body
feat: Add PostgreSQL storage backend

- Implement IStateStorage for PostgreSQL
- Add connection pooling
- Support AOT compilation
- Include migration scripts

Closes #123
```

**Format:**
- First line: `<type>: <subject>` (50 chars or less)
- Blank line
- Body: Detailed explanation (wrap at 72 chars)
- Footer: Issue references

**Types:**
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation
- `test`: Tests
- `refactor`: Code refactoring
- `perf`: Performance improvement
- `style`: Code style changes
- `chore`: Build process, dependencies

### Pull Request Process

1. **Sync with upstream:**
```bash
git fetch upstream
git rebase upstream/main
```

2. **Ensure quality:**
```bash
# Build succeeds
dotnet build -maxcpucount

# Tests pass
dotnet test

# No new warnings
dotnet build -maxcpucount -warnaserror
```

3. **Create pull request:**
- Clear title summarizing the change
- Detailed description explaining:
  - What changed
  - Why it changed
  - How to test it
- Reference related issues: `Closes #123`, `Related to #456`
- Add screenshots/examples if applicable

4. **PR template:**
```markdown
## Description
Brief summary of changes

## Changes Made
- Added X feature
- Fixed Y bug
- Updated Z documentation

## Testing
- [ ] All tests pass
- [ ] Added new tests for new functionality
- [ ] Verified AOT compatibility

## Related Issues
Closes #123

## Checklist
- [ ] Code follows project conventions
- [ ] Documentation updated
- [ ] Tests added/updated
- [ ] No breaking changes (or documented)
```

5. **Respond to feedback:**
- Address review comments promptly
- Make requested changes in new commits (don't force-push)
- Mark conversations as resolved when done

6. **Merge:**
- Maintainers will merge once approved
- Your contribution will be acknowledged in release notes

---

## Issue Reporting

### Bug Reports

Use the bug report template:

```markdown
**Describe the bug**
A clear description of what the bug is.

**To Reproduce**
Steps to reproduce:
1. Create actor with '...'
2. Call method '...'
3. Observe error '...'

**Expected behavior**
What you expected to happen.

**Actual behavior**
What actually happened.

**Environment:**
- Quark version: 0.1.0
- .NET SDK: 10.0.102
- OS: Ubuntu 22.04
- Docker: 24.0.7 (if applicable)

**Code sample:**
```csharp
var factory = new ActorFactory();
var actor = factory.CreateActor<MyActor>("test");
// Error occurs here
```

**Stack trace:**
```
System.InvalidOperationException: No factory registered
   at Quark.Core.ActorFactory.CreateActor...
```

**Additional context**
Any other relevant information.
```

### Feature Requests

Use the feature request template:

```markdown
**Is your feature request related to a problem?**
A clear description of the problem.

**Describe the solution you'd like**
What you want to happen.

**Describe alternatives you've considered**
Other approaches you've thought about.

**Use case**
Real-world scenario where this would be useful.

**Additional context**
Code examples, mockups, references to similar features in other frameworks.
```

---

## Documentation

### What to Document

- **Public APIs**: All public classes, methods, properties
- **Examples**: Working code samples
- **Guides**: Step-by-step tutorials
- **Architecture**: Design decisions and patterns

### Documentation Standards

```csharp
/// <summary>
/// Creates a new actor instance with the specified ID.
/// </summary>
/// <param name="actorId">The unique identifier for the actor.</param>
/// <typeparam name="TActor">The type of actor to create.</typeparam>
/// <returns>The created actor instance.</returns>
/// <exception cref="ArgumentNullException">
/// Thrown when <paramref name="actorId"/> is null.
/// </exception>
/// <exception cref="InvalidOperationException">
/// Thrown when no factory is registered for <typeparamref name="TActor"/>.
/// </exception>
/// <example>
/// <code>
/// var factory = new ActorFactory();
/// var actor = factory.CreateActor&lt;MyActor&gt;("actor-1");
/// </code>
/// </example>
public TActor CreateActor<TActor>(string actorId) where TActor : IActor;
```

### Wiki Pages

When updating wiki pages:

1. Use clear headings and structure
2. Include code examples
3. Cross-reference related pages
4. Keep examples practical and tested
5. Update table of contents if needed

### README Updates

Update README.md when:

- Adding major features
- Changing setup instructions
- Updating requirements
- Adding new examples

---

## Community

### Communication Channels

- **GitHub Issues**: Bug reports, feature requests
- **GitHub Discussions**: Questions, ideas, general discussion
- **Pull Requests**: Code contributions, reviews

### Getting Help

Stuck? Here's how to get help:

1. **Search documentation**: Check wiki pages
2. **Search issues**: Someone might have asked already
3. **Ask in discussions**: Start a discussion thread
4. **Open an issue**: If it's a bug or clear feature request

### Helping Others

Ways to help the community:

- Answer questions in discussions
- Review pull requests
- Test pre-release versions
- Share your Quark projects
- Write blog posts or tutorials

---

## Recognition

Contributors are recognized in:

- Release notes
- Contributors list
- Project README (for significant contributions)

Thank you for making Quark better! 🚀

---

## Quick Reference

### Workflow Summary

```bash
# 1. Fork and clone
git clone https://github.com/YOUR_USERNAME/Quark.git
cd Quark

# 2. Create branch
git checkout -b feature/my-feature

# 3. Make changes
# ... edit code ...

# 4. Test
dotnet build -maxcpucount
dotnet test

# 5. Commit
git add .
git commit -m "feat: Add my feature"

# 6. Push
git push origin feature/my-feature

# 7. Create PR on GitHub
```

### Useful Commands

```bash
# Update from upstream
git fetch upstream
git rebase upstream/main

# Clean build
dotnet clean && dotnet build -maxcpucount

# Run specific test
dotnet test --filter "FullyQualifiedName~MyTest"

# Check formatting
dotnet format --verify-no-changes

# Generate docs (if tool is configured)
dotnet tool run docfx
```

---

## See Also

- **[Getting Started](Getting-Started)** - User setup guide
- **[FAQ](FAQ)** - Common issues
- **[API Reference](API-Reference)** - Complete API docs
- **[Examples](Examples)** - Code samples

---

**Questions?** Open a [discussion](https://github.com/thnak/Quark/discussions) or contact the maintainers.

**Thank you for contributing to Quark!** ✨
