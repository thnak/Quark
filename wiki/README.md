# Quark Wiki Documentation

This directory contains comprehensive documentation for the Quark framework that can be published to the GitHub wiki.

## Wiki Structure

### Core Documentation
- **[Home.md](Home.md)** - Landing page with overview and quick links
- **[Getting-Started.md](Getting-Started.md)** - Installation and first actor tutorial
- **[Actor-Model.md](Actor-Model.md)** - Actor fundamentals, lifecycle, and turn-based concurrency
- **[Supervision.md](Supervision.md)** - Parent-child hierarchies, fault tolerance, and strategies
- **[Persistence.md](Persistence.md)** - State management, storage backends, and serialization

### Advanced Features
- **[Streaming.md](Streaming.md)** - Reactive streams, pub/sub patterns, and implicit subscriptions
- **[Clustering.md](Clustering.md)** - Distributed actors, Redis membership, and gRPC transport
- **[Timers-and-Reminders.md](Timers-and-Reminders.md)** - Temporal services, scheduling, and persistence
- **[Source-Generators.md](Source-Generators.md)** - AOT compatibility and compile-time code generation

### Reference
- **[API-Reference.md](API-Reference.md)** - Complete API documentation for interfaces and classes
- **[Examples.md](Examples.md)** - Code samples, patterns, and working examples
- **[FAQ.md](FAQ.md)** - Common questions and troubleshooting
- **[Contributing.md](Contributing.md)** - Contributor guidelines and development setup

## Publishing to GitHub Wiki

To publish these files to the GitHub wiki:

### Option 1: Manual Upload (Recommended)

1. **Enable the wiki** on GitHub:
   - Go to repository Settings → Features → Enable Wiki

2. **Clone the wiki repository**:
   ```bash
   git clone https://github.com/thnak/Quark.wiki.git
   ```

3. **Copy the wiki files**:
   ```bash
   cp wiki/*.md Quark.wiki/
   cd Quark.wiki
   ```

4. **Commit and push**:
   ```bash
   git add .
   git commit -m "Add comprehensive Quark documentation"
   git push origin master
   ```

### Option 2: Using the GitHub UI

1. Navigate to the **Wiki** tab in your GitHub repository
2. Click **"Create the first page"** or **"New Page"**
3. For each markdown file:
   - Create a new page with the filename (without .md extension)
   - Copy and paste the markdown content
   - Save the page

### Option 3: Automated Script

Create a script to automate the process:

```bash
#!/bin/bash
# publish-wiki.sh

# Clone wiki repository
git clone https://github.com/thnak/Quark.wiki.git temp-wiki
cd temp-wiki

# Copy wiki files
cp ../wiki/*.md .

# Commit and push
git add .
git commit -m "Update Quark documentation"
git push origin master

# Cleanup
cd ..
rm -rf temp-wiki
```

Run with:
```bash
chmod +x publish-wiki.sh
./publish-wiki.sh
```

## Wiki Content Overview

### Total Content
- **13 comprehensive pages**
- **~120 KB of documentation**
- **3,500+ lines of content**
- **100+ code examples**
- **50+ diagrams and tables**

### Coverage
✅ **Complete** - All major features documented  
✅ **Practical** - Real-world examples and patterns  
✅ **Troubleshooting** - Common issues and solutions  
✅ **AOT-Focused** - Native AOT best practices  
✅ **Reference** - Complete API documentation  
✅ **Contributor-Friendly** - Clear guidelines  

## Maintaining the Wiki

### When to Update

Update the wiki documentation when:
- Adding new features or APIs
- Changing existing behavior
- Discovering common issues
- Improving examples or clarifications
- Releasing new versions

### Update Process

1. **Edit wiki files** in this directory
2. **Test locally** (use a markdown previewer)
3. **Commit to main repository**
4. **Publish to wiki** using one of the methods above

### Version Sync

Keep wiki synchronized with code:
- Reference latest stable version
- Update examples when API changes
- Add deprecation notices for old features
- Document breaking changes prominently

## Navigation Structure

The wiki uses this navigation hierarchy:

```
Home
├── Getting Started
│   ├── Prerequisites
│   ├── Installation
│   └── First Actor
│
├── Core Concepts
│   ├── Actor Model
│   ├── Supervision
│   ├── Persistence
│   └── Timers and Reminders
│
├── Advanced Features
│   ├── Streaming
│   ├── Clustering
│   └── Source Generators
│
└── Reference
    ├── API Reference
    ├── Examples
    ├── FAQ
    └── Contributing
```

## Linking Between Pages

Use GitHub wiki links (without the .md extension):

```markdown
See [Actor Model](Actor-Model) for details.
Check out [Getting Started](Getting-Started) to begin.
```

## Search Optimization

Each page includes:
- ✅ Clear titles and headers
- ✅ Keywords in first paragraph
- ✅ Table of contents for long pages
- ✅ Code examples with syntax highlighting
- ✅ Cross-references to related topics

## Feedback and Improvements

To suggest improvements:
1. Open an issue in the main repository
2. Submit a PR with documentation changes
3. Start a discussion in GitHub Discussions

## License

This documentation is part of the Quark project and is licensed under the MIT License.

---

**Last Updated**: 2026-01-29  
**Quark Version**: 0.1.0-alpha  
**Pages**: 13  
**Total Size**: ~120 KB
