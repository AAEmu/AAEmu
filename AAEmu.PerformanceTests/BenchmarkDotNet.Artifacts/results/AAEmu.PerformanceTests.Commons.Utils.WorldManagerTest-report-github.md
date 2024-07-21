```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.22631.3880/23H2/2023Update/SunValley3)
AMD Ryzen 9 5950X, 1 CPU, 32 logical and 16 physical cores
.NET SDK 8.0.303
  [Host]     : .NET 8.0.7 (8.0.724.31311), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.7 (8.0.724.31311), X64 RyuJIT AVX2


```
| Method    | Mean     | Error   | StdDev   |
|---------- |---------:|--------:|---------:|
| LoadAsync | 388.8 ms | 7.64 ms | 11.44 ms |
| Load      | 376.7 ms | 7.44 ms | 12.01 ms |
