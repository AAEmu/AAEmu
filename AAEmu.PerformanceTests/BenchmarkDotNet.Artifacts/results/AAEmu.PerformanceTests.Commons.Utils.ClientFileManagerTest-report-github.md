```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.22631.3880/23H2/2023Update/SunValley3)
AMD Ryzen 9 5950X, 1 CPU, 32 logical and 16 physical cores
.NET SDK 8.0.303
  [Host]     : .NET 8.0.7 (8.0.724.31311), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.7 (8.0.724.31311), X64 RyuJIT AVX2


```
| Method               | Mean      | Error     | StdDev    |
|--------------------- |----------:|----------:|----------:|
| GetFileAsStringAsync | 22.345 ns | 0.1408 ns | 0.1248 ns |
| GetFileAsString      |  2.837 ns | 0.0200 ns | 0.0187 ns |
