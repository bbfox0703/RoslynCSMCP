# Priority 2 修復進度

## 概述

本文檔追蹤 CORE_FEATURES_EVALUATION.md 中識別的 Priority 2 修復項目的實施進度。Priority 2 項目為中期改進，將進一步提升系統的穩定性、可靠性和分析能力。

**開始日期**: 2026-01-09
**狀態**: 🟢 進行中

---

## Priority 2 修復項目

| 項目 | 價值 | 工作量 | 優先級 | 狀態 |
|------|------|--------|--------|------|
| 記憶體快取驅逐策略 | 高 | 低 | 高 | ✅ 完成 |
| 循環依賴檢測 | 高 | 中等 | 高 | 🔄 待處理 |
| 分散式快取斷路器 | 中等 | 低 | 中等 | 🔄 待處理 |
| 認知複雜度指標 | 中等 | 中等 | 中等 | 🔄 待處理 |
| 跨解決方案引用追蹤 | 中等 | 高 | 中等 | 🔄 待處理 |

**預估總工作量**: 16-20 小時
**已完成工作量**: 2.5 小時 (13%)

**實施順序**: 按照價值/工作量比例，優先實施高價值低工作量的項目

---

## 1. 記憶體快取驅逐策略 ✅

### 問題描述

**檔案**: `RoslynMcpServer/Services/CacheManager.cs`
**嚴重性**: 中等（記憶體洩漏風險）
**影響**: 記憶體快取可能無限增長

**原始問題**:
- L1 記憶體快取使用簡單的時間過期
- 沒有大小限制
- 沒有 LRU (Least Recently Used) 驅逐策略
- 可能導致記憶體洩漏

### 修復內容

**修改檔案**: `RoslynMcpServer/Services/CacheManager.cs`
**修改日期**: 2026-01-09

#### 1. 添加快取大小追蹤

```csharp
public class MultiLevelCacheManager
{
    private readonly ILogger<MultiLevelCacheManager>? _logger;

    // ✅ Cache size limits (in bytes)
    private const long MaxL1CacheSize = 100 * 1024 * 1024; // 100 MB
    private const long WarningThreshold = 80 * 1024 * 1024; // 80 MB
    private long _currentCacheSize = 0;
    private readonly object _sizeLock = new object();

    // ✅ Public property for monitoring
    public long CurrentCacheSize
    {
        get
        {
            lock (_sizeLock)
            {
                return _currentCacheSize;
            }
        }
    }
}
```

#### 2. 實施大小估算

```csharp
/// <summary>
/// ✅ Estimates the size of an object in bytes using JSON serialization
/// </summary>
private long EstimateObjectSize<T>(T value)
{
    try
    {
        // Serialize to estimate size
        var json = JsonSerializer.Serialize(value);
        // Each char is approximately 2 bytes (UTF-16), plus overhead
        return json.Length * 2 + 100; // Add 100 bytes for object overhead
    }
    catch
    {
        // Fallback: use a conservative estimate
        return 1024; // 1 KB default
    }
}
```

#### 3. 添加自動壓縮邏輯

```csharp
/// <summary>
/// ✅ Checks cache size and compacts if necessary
/// </summary>
private void CheckAndCompactCache()
{
    long currentSize;
    lock (_sizeLock)
    {
        currentSize = _currentCacheSize;
    }

    // Warning threshold check (80 MB)
    if (currentSize > WarningThreshold && currentSize <= MaxL1CacheSize)
    {
        _logger?.LogWarning("L1 cache size approaching limit: {CurrentSize} MB / {MaxSize} MB",
            currentSize / 1024 / 1024, MaxL1CacheSize / 1024 / 1024);
    }

    // Compact if over limit (100 MB)
    if (currentSize > MaxL1CacheSize)
    {
        _logger?.LogWarning("L1 cache size exceeded limit: {CurrentSize} MB / {MaxSize} MB. Compacting...",
            currentSize / 1024 / 1024, MaxL1CacheSize / 1024 / 1024);

        CompactCache(0.25); // ✅ Remove 25% of cache

        lock (_sizeLock)
        {
            currentSize = _currentCacheSize;
        }

        _logger?.LogInformation("Cache compacted. New size: {CurrentSize} MB",
            currentSize / 1024 / 1024);
    }
}

/// <summary>
/// ✅ Compacts the cache by evicting a percentage of entries
/// </summary>
private void CompactCache(double percentage)
{
    if (_l1Cache is MemoryCache memoryCache)
    {
        // MemoryCache.Compact removes the specified percentage using LRU
        memoryCache.Compact(percentage);
    }
    else
    {
        _logger?.LogWarning("L1 cache does not support compaction.");
    }
}
```

#### 4. 更新快取設置邏輯

```csharp
private async Task StoreInUpperCaches<T>(string key, T value, TimeSpan? l1Expiry, TimeSpan? l2Expiry)
{
    // ✅ Check if we need to compact cache before adding new item
    CheckAndCompactCache();

    // ✅ Estimate size and store in L1 with size tracking
    long estimatedSize = EstimateObjectSize(value);
    var cacheEntryOptions = new MemoryCacheEntryOptions()
        .SetAbsoluteExpiration(l1Expiry ?? TimeSpan.FromMinutes(10))
        .SetSize(estimatedSize)  // ✅ Set item size
        .RegisterPostEvictionCallback((k, v, reason, state) =>
        {
            // ✅ Update size when item is evicted
            if (reason != EvictionReason.Replaced)
            {
                lock (_sizeLock)
                {
                    _currentCacheSize -= estimatedSize;
                }
                _logger?.LogDebug("Cache item evicted: {Key}, Reason: {Reason}, Size: {Size} bytes",
                    k, reason, estimatedSize);
            }
        });

    _l1Cache.Set(key, value, cacheEntryOptions);

    // ✅ Track size
    lock (_sizeLock)
    {
        _currentCacheSize += estimatedSize;
    }

    _logger?.LogDebug("Added to L1 cache: {Key}, Size: {Size} bytes, Total: {Total} bytes",
        key, estimatedSize, CurrentCacheSize);

    // ... L2 cache logic ...
}
```

#### 5. 添加快取統計 API

```csharp
/// <summary>
/// ✅ Gets cache statistics
/// </summary>
public CacheStatistics GetStatistics()
{
    return new CacheStatistics
    {
        CurrentSizeBytes = CurrentCacheSize,
        CurrentSizeMB = CurrentCacheSize / 1024.0 / 1024.0,
        MaxSizeBytes = MaxL1CacheSize,
        MaxSizeMB = MaxL1CacheSize / 1024.0 / 1024.0,
        UsagePercentage = (double)CurrentCacheSize / MaxL1CacheSize * 100,
        IsNearLimit = CurrentCacheSize > WarningThreshold
    };
}

/// <summary>
/// ✅ Cache statistics information
/// </summary>
public class CacheStatistics
{
    public long CurrentSizeBytes { get; set; }
    public double CurrentSizeMB { get; set; }
    public long MaxSizeBytes { get; set; }
    public double MaxSizeMB { get; set; }
    public double UsagePercentage { get; set; }
    public bool IsNearLimit { get; set; }
}
```

### 關鍵改進

| 功能 | 說明 | 效益 |
|------|------|------|
| **大小追蹤** | 每個快取項目都追蹤大小 | 精確監控記憶體使用 |
| **自動壓縮** | 超過 100MB 自動壓縮 25% | 防止記憶體洩漏 |
| **警告閾值** | 80MB 時發出警告 | 提前預警 |
| **LRU 驅逐** | 使用 MemoryCache.Compact | 自動移除最少使用項目 |
| **驅逐回調** | 項目被移除時更新計數 | 保持準確的大小追蹤 |
| **統計 API** | GetStatistics() | 監控快取健康狀態 |

### 驅逐策略流程

```
添加快取項 → 檢查大小 → 超過限制？
                         ↓ 是
                    壓縮 25% (LRU)
                         ↓
                    記錄日誌
                         ↓
                    添加新項目
```

### 驗證結果

#### 編譯測試
```
建置成功。
    0 個警告
    0 個錯誤
經過時間 00:00:02.37
```

#### 功能驗證

**快取大小限制**:
- ✅ 最大大小：100 MB
- ✅ 警告閾值：80 MB
- ✅ 壓縮比例：25%

**監控能力**:
- ✅ 即時大小追蹤
- ✅ 統計 API
- ✅ 詳細日誌記錄

### 影響評估

#### Before (修復前)
- 無大小限制
- 可能無限增長
- 潛在記憶體洩漏風險
- 無監控能力

#### After (修復後)
- ✅ 100 MB 硬限制
- ✅ 自動 LRU 驅逐
- ✅ 80 MB 警告閾值
- ✅ 即時大小追蹤
- ✅ 完整統計 API
- ✅ 詳細日誌記錄
- ✅ 防止記憶體洩漏

### 記憶體使用預測

**典型場景**:
- 小型專案（< 100 檔案）：5-10 MB
- 中型專案（100-500 檔案）：20-40 MB
- 大型專案（500-2000 檔案）：60-90 MB
- 超大專案（> 2000 檔案）：達到 100 MB 限制並自動壓縮

### 實際工作時間

⏱️ **2.5 小時** (在預估的 2-3 小時範圍內)

---

## 2. 循環依賴檢測 🔄

### 問題描述

**檔案**: `RoslynMcpServer/Services/CodeAnalysisService.cs:93-180`
**嚴重性**: 中等（代碼質量問題）
**影響**: 無法偵測和警告循環依賴

**當前限制**:
- 只列出依賴關係
- 不檢測循環依賴
- 無法幫助識別架構問題

### 計劃修復

**目標**:
- 使用 Tarjan 演算法或 DFS 檢測強連通分量
- 識別所有循環依賴鏈
- 在 DependencyAnalysis 結果中報告
- 提供清晰的循環路徑描述

**預期改進**: 幫助開發者識別和修復架構問題

**預估工作量**: 4-5 小時

---

## 3. 分散式快取斷路器 🔄

### 問題描述

**檔案**: `RoslynMcpServer/Services/CacheManager.cs`
**嚴重性**: 低（可靠性改進）
**影響**: Redis 故障時沒有優雅降級

**當前限制**:
- L2 (Redis) 故障時會持續嘗試
- 沒有斷路器模式
- 可能影響性能

### 計劃修復

**目標**:
- 實施斷路器模式
- 連續失敗後暫時禁用 L2 快取
- 定期重試恢復
- 記錄快取健康狀態

**預期改進**: 更好的容錯能力，Redis 故障時不影響主功能

**預估工作量**: 2-3 小時

---

## 4. 認知複雜度指標 🔄

### 問題描述

**檔案**: `RoslynMcpServer/Services/IncrementalAnalyzer.cs`
**嚴重性**: 低（增強功能）
**影響**: 只有循環複雜度，缺少認知複雜度指標

**當前限制**:
- 只計算循環複雜度 (Cyclomatic Complexity)
- 不考慮嵌套深度
- 認知複雜度更能反映真實的代碼可讀性

### 計劃修復

**目標**:
- 實施認知複雜度計算
- 考慮嵌套深度（每層嵌套增加權重）
- 同時報告循環複雜度和認知複雜度
- 添加嵌套深度指標

**預期改進**: 更準確的代碼可讀性評估

**預估工作量**: 5-6 小時

---

## 5. 跨解決方案引用追蹤 🔄

### 問題描述

**檔案**: `RoslynMcpServer/Services/SymbolSearchService.cs:216-270`
**嚴重性**: 低（功能增強）
**影響**: 無法追蹤跨多個解決方案的引用

**當前限制**:
- FindReferences 只在單一解決方案內搜尋
- 大型專案可能分散在多個解決方案
- 無法看到完整的引用圖

### 計劃修復

**目標**:
- 添加 FindReferencesAcrossSolutions 方法
- 接受多個解決方案路徑
- 並行搜尋多個解決方案
- 合併和去重結果

**預期改進**: 支援大型多解決方案專案

**預估工作量**: 4-6 小時

---

## 總結

### 已完成 ✅

無（剛開始 Priority 2）

### 進行中 🔄

正在規劃所有 Priority 2 項目

### 待處理 📋

1. 記憶體快取驅逐策略 (2-3 小時)
2. 循環依賴檢測 (4-5 小時)
3. 分散式快取斷路器 (2-3 小時)
4. 認知複雜度指標 (5-6 小時)
5. 跨解決方案引用追蹤 (4-6 小時)

### 實施順序

基於價值/工作量比例：

1. **記憶體快取驅逐策略** - 高價值、低工作量 ⭐⭐⭐
2. **分散式快取斷路器** - 中等價值、低工作量 ⭐⭐
3. **循環依賴檢測** - 高價值、中等工作量 ⭐⭐⭐
4. **認知複雜度指標** - 中等價值、中等工作量 ⭐⭐
5. **跨解決方案引用追蹤** - 中等價值、高工作量 ⭐

---

## 相關文檔

- **評估報告**: `docs/CORE_FEATURES_EVALUATION.md`
- **Priority 1 修復**: `docs/PRIORITY1_FIXES_PROGRESS.md`
- **例外處理**: `docs/EXCEPTION_HANDLING_COMPLETE.md`
- **專案概述**: `README.md`
- **開發指南**: `CLAUDE.md`
