# VectorVibe 🚀

A lightweight, easy-to-use vector database for .NET developers who want to build semantic search, recommendation systems, and AI-powered apps without the complexity!

```csharp
// Add text with embeddings
db.AddVector(yourEmbeddings, "Your text here");

// Search similar content
var results = await db.SearchNearestWithTextAsync(queryEmbeddings);
```

## Why VectorVibe? 🤔

Tired of overcomplicated vector databases? VectorVibe gives you:

- 💡 **Simple API** - Just add vectors and search!
- ⚡ **Lightning fast** - Optimized ANN search out-of-the-box
- 🔒 **Data safety** - Built-in CRC32 checks and WAL recovery
- 📚 **Metadata support** - Store text alongside vectors
- 🧠 **Semantic search** - Find similar content by meaning
- 🚫 **Zero dependencies** - Pure C# goodness

Perfect for: Semantic search, chatbots, recommendation engines, and AI memory systems!

## Quick Start 🚀

1. Create your vector database:
```csharp
using var db = new VectorDatabase("myData.db", compress: true);
```

2. Add some content:
```csharp
db.AddVector(GetEmbeddings("Cute puppies playing"), "Puppies playing in the park");
db.AddVector(GetEmbeddings("Tech news update"), "Latest AI breakthroughs announced");
```

3. Search for similar content:
```csharp
var query = GetEmbeddings("Funny dog videos");
var results = await db.SearchNearestWithTextAsync(query);

foreach (var (id, text) in results)
{
    Console.WriteLine($"Found: {text}");
}
```

## Features 🌟

### Text + Vector Storage
```csharp
// Add text with its vector representation
db.AddVector(yourEmbeddings, "Your text here");
```

### Semantic Search
```csharp
// Find similar content by meaning
var results = await db.SearchNearestWithTextAsync(queryVector);
```

### Data Safety
```csharp
// Automatic recovery from crashes
using var db = new VectorDatabase("data.db"); // Automatically loads existing data

// Manual save when needed
db.SaveToFile();
```

### Compression Support
```csharp
// Reduce storage by 4x (perfect for large datasets)
var db = new VectorDatabase("bigData.db", compress: true);
```

## How It Works? 🔧

1. **Add Content**  
   You add text + its vector embeddings
   ```
   [Your Text] ➡️ [Embedding Model] ➡️ [Vector] ➡️ VectorVibe
   ```

2. **Store Efficiently**  
   We optimize storage with quantization and compression

3. **Search**  
   Find similar vectors using approximate nearest neighbors

4. **Get Results**  
   Receive matching content ranked by similarity

## Real-World Examples 🎯

### Build a FAQ Bot
```csharp
// Store FAQ embeddings
db.AddVector(GetEmbeddings("Return policy"), "You have 30 days to return items");

// Answer user questions
var userQuestion = GetEmbeddings("Can I send back purchases?");
var answer = (await db.SearchNearestWithTextAsync(userQuestion)).First().Text;
```

### Create a Content Recommender
```csharp
// Recommend similar articles
var currentArticle = GetEmbeddings(articleText);
var similar = await db.SearchNearestWithTextAsync(currentArticle);
```

### Build Long-Term Memory for AI
```csharp
// Store conversation history
db.AddVector(GetEmbeddings(userMessage), userMessage);

// Recall relevant context
var context = await db.SearchNearestWithTextAsync(newUserMessage);
```

## Get Started 🛠️

1. Add VectorVibe to your project:
```bash
git clone https://github.com/pwrmind/VectorVibe.git
```

2. Check out our samples:
```csharp
// Basic usage example
using var db = new VectorDatabase("demo.db");

// Add sample data
db.AddVector(GetFakeEmbeddings("Cat videos"), "Funny cat compilation");
db.AddVector(GetFakeEmbeddings("Tech news"), "New GPU releases announced");

// Search
var results = await db.SearchNearestWithTextAsync(
    GetFakeEmbeddings("Funny animals"));
```

## Roadmap 🗺️

- [x] Core vector storage
- [x] Text metadata support
- [x] Compression
- [ ] Batch import/export
- [ ] Hybrid search (vectors + keywords)
- [ ] Server mode (HTTP API)
- [ ] .NET Package

## Contribute 🤝

Found a bug? Have a feature request? We love contributions!

1. Fork the repo
2. Create your branch (`git checkout -b cool-feature`)
3. Commit changes (`git commit -m 'Add awesome feature'`)
4. Push (`git push origin cool-feature`)
5. Open a PR

Let's build the simplest vector database for .NET together!

## License 📄

VectorVibe is MIT licensed - use it for any project, commercial or personal!

---
Made with ❤️ by .NET developers for .NET developers  

[Give it a star ⭐] | [Report an issue 🐛] | [Contribute 👨‍💻]