# LDOCE5ViewerXBenchmarks

Benchmarks for generated incremental/full-text index search performance and
plain-text loading from real dictionary archive data.

The benchmarks use real generated index data. By default they resolve the same
per-user index directory as `IndexPaths`. To benchmark a specific generated
index directory, set `LDOCE5VIEWERX_INDEX_DIR` to a directory containing:

- `incremental.db`
- `fulltext_hp/`
- `fulltext_de/`
- optionally `variations.cdb`

`DictionaryContentServiceBenchmarks` also reads real `ldoce5.data`. It uses the
data directory saved in `config.json`, or set `LDOCE5VIEWERX_DATA_DIR` to override
it.

Run benchmarks from the repository root:

```bash
dotnet run -c Release --project LDOCE5ViewerXBenchmarks/LDOCE5ViewerXBenchmarks.csproj
```

Run one benchmark type:

```bash
dotnet run -c Release --project LDOCE5ViewerXBenchmarks/LDOCE5ViewerXBenchmarks.csproj -- --filter '*FullTextSearcherBenchmarks*'
```

Run dictionary content loading benchmarks:

```bash
dotnet run -c Release --project LDOCE5ViewerXBenchmarks/LDOCE5ViewerXBenchmarks.csproj -- --filter '*DictionaryContentServiceBenchmarks*'
```
