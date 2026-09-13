# 动态背景（动图 + 轮播）设计方案

> 状态：**阶段 0 探针已完成**，方案据实测结论修订（见 §3.4 / 附录 C）
> 当前为测试版阶段：**不做配置迁移**，允许对壁纸相关字段做破坏性变更
> 范围：GIF / 动画 WebP 播放 + 多图轮播；**不含视频背景**（理由见附录 A）、**不含 APNG 播放**（决策 D7，理由见附录 B）
> 影响模块：`ObsMCLauncher.Core`（模型、解码器）、`ObsMCLauncher.Desktop`（渲染宿主、设置页）
>
> **决策 D7：放弃 APNG 播放**（理由与留档见附录 B）。G1 相应收窄为 GIF + 动画 WebP；
> APNG 在添加时会被**识别并明确拒绝**，不做静默降级。探针推翻原设计的经过见 §3.4 P0-1。

---

## 1. 目标与非目标

### 目标

| 编号 | 目标 | 验收方式 |
|-----|------|---------|
| G1 | 支持 GIF / 动画 WebP 作为主界面背景动图 | 两种格式各实测通过 |
| G2 | 支持多张背景（静态图 + 动图混排）定时轮播，带交叉淡入过渡 | 轮播间隔、顺序、过渡可视验证 |
| G3 | 静态图路径零性能回归 | 改造前后 CPU 对比，偏差 ≤ 0.5% |
| G4 | 安装包体积增量 ≤ 0.5 MB | 对比 Release 产物 |
| G5 | 设置页重做为卡片网格（仿 ClassIsland 主题页交互） | UI 走查 |
| G6 | 全生命周期省电策略，空闲时 CPU 回落到基线 | 最小化 / 失焦 / 电池场景实测 |
| G7 | APNG 被识别并给出可操作的提示，不静默降级 | 添加 APNG 时被拒收且文案正确 |

### 非目标

- 视频背景（mp4 / webm）——评估结论见附录 A
- **APNG（动画 PNG）播放**——SkiaSharp 底层不支持（§3.4 P0-1），D7 已决策放弃，理由见附录 B
- 在线 / 网络动态壁纸（涉及带宽、版权、缓存淘汰，另立需求）
- 壁纸市场 / 插件分发壁纸
- 多显示器各自独立壁纸
- 崩溃窗口、开发控制台窗口的壁纸（仅 `MainWindow` 生效）

---

## 2. 现状回顾

### 2.1 当前实现（单张静态图）

| 位置 | 职责 |
|------|------|
| `Core/Models/LauncherConfig.cs:196-212` | 6 个字段：`WallpaperEnabled` / `WallpaperPath` / `WallpaperOpacity` / `WallpaperStretch` / `WallpaperExtendToNav` / `NavBackgroundOpacity` |
| `Desktop/ViewModels/SettingsViewModel.cs:616-682` | `ApplyWallpaper()`：解码 `Bitmap` → 组装 `ImageBrush` → 写入 `Application.Current.Resources` |
| `Desktop/ViewModels/SettingsViewModel.cs:698-724` | `BrowseWallpaperAsync()`：`OpenFileDialog`，扩展名白名单 `png/jpg/jpeg/webp/bmp` |
| `Desktop/Views/MainWindow.axaml:150-154` | 一个 `Border`，`Background` 绑定 `MainWallpaperBrush`，`IsVisible` 绑定 `IsMainWallpaperVisible` |
| `Desktop/Styles/Theme.axaml:27,158` | `IsMainWallpaperVisible` 默认值与 `NavBackgroundBrush` 默认值 |

### 2.2 现有实现的三个结构性限制

1. **载体是 `ImageBrush`**——`ImageBrush.Source` 只接受 `IImage`（单帧位图），无法承载"随时间变化的帧序列"。动图必须换成**控件级宿主**。
2. **状态写在 `Application.Resources`**——`MainWallpaperBrush` / `IsMainWallpaperVisible` 是全局可变资源，靠 `Dispatcher.UIThread.Post` 手动推送。组件化为控件后应改为 ViewModel 绑定。
3. **逻辑耦合在 `SettingsViewModel`**——`ApplyWallpaper()` 同时承担"读配置""解码""写资源""算导航栏透明度"四件事。加入动图生命周期与轮播调度前必须拆分。

### 2.3 依赖现状（关键结论）

```
ObsMCLauncher.Desktop.csproj
  └─ Avalonia.Desktop 11.3.11
       └─ Avalonia.Win32 / X11 / Native
            └─ Avalonia.Skia
                 └─ SkiaSharp 3.116.1
                      ├─ SkiaSharp.NativeAssets.Win32  3.116.1
                      ├─ SkiaSharp.NativeAssets.macOS  3.116.1
                      └─ SkiaSharp.NativeAssets.Linux  2.88.9   ← 版本不匹配，见下
  └─ Avalonia.Svg.Skia 11.3.0 → SkiaSharp 3.116.1
```

实测 `obj/project.assets.json` 解析结果：

- `SkiaSharp/3.116.1`、`HarfBuzzSharp/8.3.1.1` 均为**传递依赖**
- Release 产物中已存在 `runtimes/*/native/libSkiaSharp.{dll,so,dylib}`（win-x64 9.16 MB / osx 14.51 MB / linux-x64 8.82 MB）

**因此：本次改造需要把 `SkiaSharp` 提升为直接 `PackageReference`（版本必须锁 3.116.1），但不会新增任何二进制文件。**

> ⚠️ **顺带发现的既有隐患**：`project.assets.json` 中 `SkiaSharp.NativeAssets.Linux` 解析为 **2.88.9**，而托管库与 Win32 / macOS 原生库都是 **3.116.1**。托管层与 Linux 原生层差一个大版本，跨 ABI 调用存在崩溃风险。这不是本次改造引入的，但 Linux RID 的 CI 产物校验（`.github/workflows/build.yml`）应顺带确认一下。**建议另立任务**，不阻塞本方案。

---

## 3. 核心技术结论

### 3.1 Avalonia 11 没有内置动图播放能力

- `Avalonia.Media.Imaging.Bitmap` 只解码首帧，没有 `AnimatedBitmap` 类型。
- Avalonia 官方讨论（AvaloniaUI/Avalonia #19194）中维护者的结论：**动图/Timeline 类内容应使用 `CompositionCustomVisualHandler`**，理由是它跑在合成器线程上，完全绕开 UI 线程。`Avalonia.Labs.Gif` 与 `Avalonia.Labs.Lottie` 均采用此方案。
- 备选方案是 `WriteableBitmap` + `Image` + `DispatcherTimer`，实现简单但每帧都要经 UI 线程，且 `DispatcherTimer` 精度受 UI 线程繁忙程度影响，**仅作为阶段 1 的过渡实现**。

### 3.2 SkiaSharp 3.116.1 的 `SKCodec` 能力（已用反射核对实际签名）

| 成员 | 用途 |
|------|------|
| `SKCodec.Create(Stream, out SKCodecResult)` | 从流创建解码器，带结果码 |
| `FrameCount` | 帧数；**非动画图返回 0**，动图 > 1 |
| `FrameInfo` (`SKCodecFrameInfo[]`) | 每帧的 `Duration` / `FrameRect` / `RequiredFrame` / `Blend` / `DisposalMethod` |
| `RepetitionCount` | 循环次数，**-1 表示无限循环** |
| `GetScaledDimensions(float)` | 返回 codec 接受的缩放尺寸 |
| `GetPixels(SKImageInfo, IntPtr, int rowBytes, SKCodecOptions)` | 解码到指定缓冲区 |
| `SKCodecOptions(frameIndex, priorFrame)` | `priorFrame` 指定"缓冲区里当前是第几帧" |
| `StartIncrementalDecode` / `GetScanlines` / `IncrementalDecode` | 逐行流式解码原语 |
| `EncodedFormat` | `Gif` / `Png` / `Webp` 等（**`SKEncodedImageFormat` 里没有 Apng 成员**） |

> `SKEncodedImageFormat` 枚举完整列表已核对：Bmp / Gif / Ico / Jpeg / Png / Wbmp / Webp / Pkm / Ktx / Astc / Dng / Heif / Avif / Jpegxl。**没有任何成员能表达"动画 PNG"**，印证了 APNG 必须靠内容探测而非格式枚举。

### 3.3 原生缩放的真实适用条件（实测修正）

原设计写"用 `GetScaledDimensions` 可在解码阶段就降分辨率，省掉后续一次全尺寸缩放"。实测后需要**加三个限定条件**：

| 行为 | 实测结果 |
|------|---------|
| GIF 任意比例**降**采样 | ✅ 支持。`GetScaledDimensions(0.5729)` → 1100×619，`GetPixels` 返回 `Success`，且经网格指纹验证是**真实等比降采样**（见 §3.4 P0-G） |
| WebP **降**采样 | ✅ 支持，同样验证为真缩放 |
| **任何格式的升采样** | ❌ `GetPixels` 返回 `InvalidScale`。`GetScaledDimensions(1.375)` 会直接返回源尺寸 |
| **PNG 的缩放** | ❌ 完全忽略 scale，无论传 0.05 还是 1.0 都返回源尺寸 |

**由此得到一条硬约束**：`WallpaperMaxDecodeEdge` 只能**向下**夹紧，即

```csharp
目标长边 = min(源长边, 窗口物理长边, WallpaperMaxDecodeEdge)   // 结果必然 ≤ 源长边
```

小图（如 800×450 用在 1100 宽的窗口）**不能**靠解码器放大，必须走渲染期缩放——这本来就是免费行为（GPU 采样），不构成问题，但代码里不能假设 `GetPixels` 一定能返回目标尺寸。

### 3.4 阶段 0 探针结论（实测，取代原估算）

探针工程位于 `.temp/probe/`（不进解决方案，不参与编译）。素材由 `gen_assets.py` / `gen_long.py` 生成，含合法多帧 GIF / APNG / 动画 WebP。运行环境：Windows x64，12 逻辑核，.NET 8.0.20，SkiaSharp 3.116.1。

#### P0-1：格式支持性 —— **APNG 不支持（阻塞项）**

| 格式 | `FrameCount` | 循环次数 | 逐帧信息 | 结论 |
|-----|-------------|---------|---------|------|
| GIF | 60 / 90 / 300 | -1（无限） | 完整 | ✅ 可用 |
| 动画 WebP | 40 / 300 | -1 | 完整 | ✅ 可用 |
| **APNG（`.png`）** | **0** | **0** | **无** | ❌ **不支持** |
| 静态 PNG / 静态 WebP | 0 | 0 | 无 | ✅（按静态图处理正确） |

**证据链**（三重印证，不是推测）：

1. **行为**：用 Pillow 生成的合法 APNG（`acTL` 声明 48 帧、`fcTL`/`fdAT` 块齐全、Pillow 自身能读 48 帧），SkiaSharp 读出来 `FrameCount = 0`，逐帧解码得到 1 个唯一哈希——即**只解出了首帧**。
2. **官方文档**：Skia 仓库 `rust/png/README` 明确列出差异——`SkPngCodec`：**No APNG support**；`SkPngRustCodec`：支持 APNG，但需要构建开关 `skia_use_rust_png_decode = true`。
3. **构建**：SkiaSharp 3.116.1 的预编译原生库走的是 libpng 版 `SkPngCodec` 路径，未启用 Rust PNG 解码器。

> 我在探针过程中曾用 `strings` + `grep` 在二进制里找符号，**这条路在本环境不可用**：`strings` 命令不存在，且 `grep -a` 对 C++ 符号名做正对照时也返回 0（SkiaSharp 原生库导出的是 `sk_codec_*` 形式的小写 C ABI）。两次符号搜索结果均为假阴性，已作废——最终结论只依赖上面三条可复现证据。

**影响**：APNG 原本是明确要求支持的格式（G1）。已由 **D7** 决策为「放弃播放、保留识别」——
> 由 `BackgroundResolver` 扫 `acTL` 判定，命中则在添加时拒收并给出可操作提示。落地细节见 §5.5。

#### P0-2：缓冲区所有权 —— 得到一个比"双缓冲还是三缓冲"更硬的约束

原设计在纠结"Skia 会不会延迟引用 CPU 缓冲区，需不需要三缓冲"。实测发现**真正的问题在别处**：

- `SKImage.FromPixels(info, ptr, rowBytes)` → `IsTextureBacked = False`，`IsLazyGenerated = False`
- 光栅路径下 `drawImage` 后改写源缓冲区，快照哈希不变（`E8AACA6A4429F55C` → `E8AACA6A4429F55C`），说明光栅后端在 draw 时即确定像素
- GPU 上下文**无法在无窗口进程中创建**（`GRContext.CreateGl()` 返回 null），所以 GPU 纹理上传时机**无法在探针内验证**

> 因此"光栅路径安全"这个结论**不能外推到 GPU 合成器**。GPU 后端的纹理上传会延迟到 flush，那才是风险窗口。

**但真正推翻原设计的是 P0-3 带来的这条约束**：Skia 官方测试代码里写着

```cpp
options.fPriorFrame = 0;   // pixmap contains the first frame before getPixels call
```

即 **`PriorFrame` 的语义是"目标缓冲区里现在装着第 N 帧"**——增量解码是**原地**把第 N 帧的差分应用到缓冲区上。于是"三缓冲轮流写入 + 增量解码"这个组合**根本无法成立**：缓冲区轮换后，待写的缓冲区里装的不是前一帧，增量解码的前提就不满足了。

**修订后的架构**（见 §5.3）：

```
累积解码缓冲 ×1   ← 增量解码原地进行，永远保持"当前已合成帧"
呈现缓冲     ×2   ← 从累积缓冲拷贝过来后移交合成器，与解码器无共享
```

合计 3 帧内存，既满足增量解码的前置条件，又让呈现侧的缓冲区**彻底不归解码器管**，竞态从设计上消失（而不是靠"赌合成器不会晚到一帧"）。代价是每帧一次 memcpy，实测 `SKImage.FromPixelCopy` 在 1100×700 / 2.94 MB 上耗时 **0.524 ms**，完全可接受。

#### P0-3：单帧解码耗时（实测中位数 / P95）

| 素材 | 解码方式 | 中位数 | P95 | 整轮耗时 | @24fps 单核 |
|-----|---------|-------|-----|---------|-----------|
| 1920×1080 GIF | 原尺寸，独立解码 | 10.23 ms | 16.99 ms | 539 ms | **24.5%** |
| 1920×1080 GIF | 原尺寸，**PriorFrame 增量** | **0.49 ms** | 1.00 ms | 31 ms | **1.2%** |
| 1920×1080 GIF | 缩放到 1100×619，独立解码 | 7.42 ms | 11.68 ms | 412 ms | 17.8% |
| 1920×1080 GIF | 缩放到 1100×619，**PriorFrame 增量** | **0.30 ms** | 0.52 ms | 21 ms | **0.7%** |
| 1100×700 GIF | 原尺寸，独立解码 | 4.91 ms | 8.57 ms | 398 ms | 11.8% |
| 1100×700 GIF | 原尺寸，**PriorFrame 增量** | **0.19 ms** | 0.36 ms | 19 ms | **0.5%** |
| 800×450 动画 WebP | 原尺寸，独立解码 | 1.37 ms | 2.72 ms | 59 ms | 3.3% |
| 800×450 动画 WebP | 原尺寸，**PriorFrame 增量** | **0.44 ms** | 0.68 ms | 19 ms | **1.1%** |

**三个结论**：

1. **`PriorFrame` 增量解码带来约 20–27× 加速**（1080p：10.23 ms → 0.49 ms）。这批素材的脏矩形平均只占全帧 **21.8%–22.6%**，增量解码真正吃到了这个红利。
2. 原文档 §6.2 估算的 "1080p @24fps 占 12–24% 单核"**只对"不做增量解码"成立**（实测 24.5%，吻合）。做增量后降到 **1.2%**，差一个数量级。
3. PNG 素材因为 `FrameCount = 0`，所有耗时数字无意义——再次印证 APNG 不可用。

#### P0-F：解码器是否偷偷缓存全部帧（内存结论的守门测试）

这个测试很重要：如果 `PriorFrame` 的加速是"Skia 内部把解码过的帧全缓存了"换来的，那内存就会随帧数线性增长，§6.1 的整个内存预算都要重写。用 300 帧素材实测：

| 素材 | 帧数 | 若全部驻留 | 实测私有字节峰值（独立解码） | 实测峰值（PriorFrame） | 帧数 300 帧时内存走势 |
|-----|-----|-----------|------------------------|---------------------|-------------------|
| long_1100x700.gif | 300 | 881 MB | 12 MB | 12 MB | 完全平坦 |
| long_1080p.gif | 300 | 2373 MB | 17 MB | 17 MB | 完全平坦 |
| long.webp | 300 | 412 MB | 12 MB | 11 MB | 完全平坦 |

**结论：Skia 不缓存全帧，内存与帧数无关。** §6.1 的流式结论成立，可以放心用。

同时做了**增量解码的像素正确性验证**：

| 素材 | 一致帧数 | 结论 |
|-----|---------|------|
| long_1100x700.gif | 300 / 300 | 逐帧哈希与独立解码完全一致 |
| long_1080p.gif | 300 / 300 | 一致 |
| long.webp | 300 / 300 | 一致 |
| anim_1080p.gif | 60 / 60 | 一致 |
| anim_dirty.webp | 40 / 40 | 一致 |

即**增量路径不仅快，而且正确**——可以放心把它作为主路径，而不是"性能模式"选项。

#### P0-G：原生缩放的正确性（是真缩放还是偷偷裁剪）

用 8×8 网格均值做与分辨率无关的内容指纹，和"换一帧"的差异值做标尺：

| 素材 | 变换 | 网格差异（真缩放对比） | 标尺 | 判定 |
|-----|------|------------------|-----|------|
| 1920×1080 GIF → 1100×619 | scale 0.5729 | 0.0 | 0.2（换帧） | **真缩放** ✓ |
| 1920×1080 GIF → 480×270 | scale 0.25 | 0.1 | 0.2（换帧） | **真缩放** ✓ |
| 800×450 WebP → 400×225 | scale 0.5 | 0.1 | 0.3（换帧） | **真缩放** ✓ |

测试图带纵向渐变，裁剪会显著打乱网格均值分布，所以这个测试对"裁剪"是敏感的。差异 0.0–0.1 说明是真实的等比降采样。

> 说明：标尺值（0.2–0.3）偏小是因为该测试图帧间变化本就平缓，作为"完全不同"的判据偏弱。判定"是真缩放"主要依据是差异值接近 0 而非接近标尺。

---

## 4. 配置模型设计

### 4.1 字段变更（`Core/Models/LauncherConfig.cs`）

**移除**：`WallpaperPath`（`string?`）——由 `WallpaperItems` 取代。测试版阶段不做迁移，旧配置里该字段会被 `JsonSerializer` 静默忽略，壁纸回落到"未设置"状态。

**保留不变**：`WallpaperEnabled` / `WallpaperOpacity` / `WallpaperStretch` / `WallpaperExtendToNav` / `NavBackgroundOpacity`。

**新增**：

```csharp
/// <summary>壁纸条目列表（替代原 WallpaperPath；空表示无壁纸）</summary>
public List<WallpaperItem> WallpaperItems { get; set; } = new();

/// <summary>是否播放动图动画（false 时只显示首帧）</summary>
public bool WallpaperPlayAnimated { get; set; } = true;

/// <summary>动图帧率上限（1-60，默认 24）</summary>
public int WallpaperMaxFps { get; set; } = 24;

/// <summary>解码长边上限像素（0=不限制，默认 1920）。
/// 只能向下夹紧：解码尺寸 = min(源长边, 窗口物理长边, 本值)，见 §3.3</summary>
public int WallpaperMaxDecodeEdge { get; set; } = 1920;

/// <summary>轮播间隔秒数（0=不轮播，默认 0）。UI 提供预设档位，也允许自定义任意秒数</summary>
public int WallpaperSlideIntervalSeconds { get; set; } = 0;

/// <summary>轮播顺序：0=顺序 1=随机 2=每次启动随机起点</summary>
public int WallpaperSlideMode { get; set; } = 0;

/// <summary>轮播过渡时长毫秒（0-2000，默认 600）</summary>
public int WallpaperTransitionMs { get; set; } = 600;

/// <summary>窗口失焦时暂停动图</summary>
public bool WallpaperPauseOnUnfocused { get; set; } = false;

/// <summary>电池供电时暂停动图</summary>
public bool WallpaperPauseOnBattery { get; set; } = true;
```

### 4.2 条目模型（新增 `Core/Models/WallpaperItem.cs`）

```csharp
public sealed class WallpaperItem
{
    /// <summary>本地文件绝对路径</summary>
    public string Path { get; set; } = "";

    /// <summary>条目唯一标识（用于 UI 选中态与拖拽排序，不参与持久化语义）</summary>
    [JsonIgnore] public string Id { get; init; } = Guid.NewGuid().ToString("N");
}

/// <summary>探测得到的实际类型（不持久化，每次按文件指纹解析）</summary>
public enum WallpaperKind
{
    Unknown, Static, Gif, AnimatedWebP,

    /// <summary>识别出是 APNG，但当前版本不播放（D7）。仅用于给出提示，不参与解码。</summary>
    ApngUnsupported
}
```

**设计要点：为什么 `Kind` 不落盘？**

APNG 的文件扩展名是 `.png`，与静态 PNG 完全无法从文件名区分；`.webp` 同样既可能是静态也可能是动画。**必须按内容探测**，落盘一个可能过期的 `Kind` 只会引入不一致。探测结果按 `(路径, 文件大小, 最后写入时间)` 三元组指纹缓存在内存字典 + 缩略图目录的 sidecar `.json` 中。

**探测顺序**：

```
1. 读前 N 字节判定容器：
     "\x89PNG\r\n\x1a\n" → PNG 家族（可能是静态 PNG，也可能是 APNG）
     "GIF8"              → GIF
     "RIFF"...."WEBP"    → WebP 家族
     其他                 → 静态图（交给 Avalonia Bitmap 处理）
2. 若为 PNG 家族：扫块找 acTL
     有 acTL → WallpaperKind.ApngUnsupported（帧数取 acTL 的 num_frames，仅用于提示文案）
     无 acTL → WallpaperKind.Static
3. 若为 GIF / WebP：用 SKCodec.FrameCount 判定
     > 1 → Gif / AnimatedWebP
     否则 → Static
```

第 2 步**必须自己扫 `acTL`**，不能依赖 `SKCodec.FrameCount`——它对 APNG 返回 0，会把 APNG 误判成静态图。那样用户拖进来一张 APNG，得到的是一张不动的图，还不知道为什么。这正是 D7 要避免的静默降级。

### 4.3 破坏性变更与兼容处理

**测试版阶段明确不做迁移**，因此：

- `WallpaperPath` 直接删除，不保留兼容分支，不写迁移代码
- 老配置文件中残留的 `WallpaperPath` 字段被 `JsonSerializer.Deserialize` 静默忽略（`LauncherConfig` 未启用 `UnmappedMemberHandling.Disallow`），不会抛异常
- 已启用旧壁纸的用户升级后壁纸失效，需重新选择——**这是测试版可接受的行为**
- 新增字段因反序列化缺失即取默认值，天然向后兼容，无需额外处理

唯一需要保留的语义收敛：`WallpaperEnabled` 只表示"开关意愿"，实际生效条件为：

```csharp
bool IsWallpaperActive => _config.WallpaperEnabled && _config.WallpaperItems.Count > 0;
```

这样"列表被清空但开关仍开着"不会产生歧义——UI 显示空态，渲染层不加载任何内容。

> 如果后续进入正式版需要迁移，再补一节 `WallpaperPath → WallpaperItems` 的提升逻辑即可，届时不影响本轮设计。

---

## 5. 架构设计

### 5.1 分层

```
                     ┌───────────────────────────────────────┐
   Core              │  BackgroundResolver                   │  容器嗅探 + APNG acTL 扫描 → WallpaperKind
                     │  IAnimatedImageDecoder   ◄── 接缝      │  单实现；保留仅为可脱离原生库做单测
                     │   └─ SkiaAnimatedImageDecoder         │  GIF / 动画 WebP，SKCodec + PriorFrame 增量
                     │  AnimatedImageInfo                    │  统一探测结果 DTO
                     │  FramePacingPlan                      │  帧时长 → 播放时刻表（含跳帧判定）
                     │  WallpaperThumbnailer                 │  首帧缩略图 + 磁盘缓存
                     └───────────────────────────────────────┘
                                      │
                     ┌───────────────────────────────────────┐
   Desktop           │  WallpaperService                     │  应用/清理、暂停策略、生命周期订阅
                     │  WallpaperRotationScheduler           │  轮播定时、顺序/随机、动图播完再切
                     └───────────────────────────────────────┘
                                      │
                     ┌───────────────────────────────────────┐
                     │  WallpaperHost (Control)              │  三层叠加：当前层 / 过渡层
                     │   ├─ StaticLayer  (Image)             │
                     │   ├─ AnimatedLayer (Composition 处理器)│
                     │   └─ CrossFadeDriver                  │
                     └───────────────────────────────────────┘
                                      │
                     MainWindow.axaml  ← 替换现有静态 Border
```

**关于 `IAnimatedImageDecoder`**：D7 之后只剩 Skia 一个实现，这个接缝看似多余。保留它的唯一理由是**单测**——`Core.Tests` 里跑 SkiaSharp 需要加载原生库（§9.2），有了接口就能构造假解码器，把帧调度、背压、暂停策略这些逻辑单独测掉，不必依赖原生库可用。上层（渲染宿主、轮播调度、暂停策略）也因此不感知格式差异：

```csharp
public interface IAnimatedImageDecoder : IDisposable
{
    AnimatedImageInfo Info { get; }
    /// <summary>把第 index 帧解码到 target 指向的缓冲区（原地增量）。</summary>
    /// <param name="priorFrameIndex">target 缓冲区当前已包含的已合成帧序号；-1 表示内容未定义</param>
    DecodeResult DecodeFrame(int index, IntPtr target, int rowBytes, int width, int height, int priorFrameIndex);
}
```

### 5.2 文件清单

**新增（Core）**

| 文件 | 职责 |
|------|------|
| `Models/WallpaperItem.cs` | 条目模型 + `WallpaperKind` 枚举 |
| `Media/BackgroundResolver.cs` | 容器嗅探（PNG/GIF/WebP 魔数）、APNG `acTL` 扫描、`SKCodec` 探测、文件指纹与结果缓存 |
| `Media/AnimatedImageInfo.cs` | 探测结果 DTO：`Kind` / `Width` / `Height` / `FrameCount` / `TotalDuration` / `LoopCount` |
| `Media/IAnimatedImageDecoder.cs` | 解码接缝。当前只有 Skia 一个实现，保留是为了单测能脱离原生库构造假解码器 |
| `Media/SkiaAnimatedImageDecoder.cs` | `SKCodec` 实现；**默认走 `PriorFrame` 增量路径**；含独立解码回退 |
| `Media/FramePacingPlan.cs` | 由帧 `Duration` 序列 + 帧率上限生成播放时刻表（含跳帧判定与"跳帧后 `priorFrame` 如何取值"） |
| `Media/WallpaperThumbnailer.cs` | 首帧解码 + 等比缩放到缩略图尺寸 + 磁盘缓存与 LRU 淘汰 |

**新增（Desktop）**

| 文件 | 职责 |
|------|------|
| `Controls/AnimatedImagePresenter.cs` | `CompositionCustomVisualHandler` 实现；累积缓冲 + 双呈现缓冲；`OnRender` 中经 `ISkiaSharpApiLeaseFeature` 直接绘制 |
| `Controls/WallpaperHost.cs` | 对外统一宿主控件，内部两层 `AnimatedImagePresenter` / `Image`，负责交叉淡入 |
| `Services/WallpaperService.cs` | 从 `SettingsViewModel` 抽出的壁纸应用逻辑；暂停策略；`WindowState` / `IsActive` / 电源状态订阅 |
| `Services/PowerStatusMonitor.cs` | 跨平台电池状态（Windows `GetSystemPowerStatus` / Linux `/sys/class/power_supply` / macOS `IOPSCopyPowerSourcesInfo`） |
| `ViewModels/WallpaperItemViewModel.cs` | 卡片展示模型（缩略图、文件名、格式徽标、选中态、加载态） |
| `Styles/WallpaperStyles.axaml` | 仅卡片网格相关样式；参数行外观由 FluentAvalonia 主题提供 |

**修改**

| 文件 | 变更 |
|------|------|
| `Core/Models/LauncherConfig.cs` | 删除 `WallpaperPath`，新增 8 个字段（测试版，无迁移逻辑） |
| `Desktop/ObsMCLauncher.Desktop.csproj` | 新增 `<PackageReference Include="SkiaSharp" Version="3.116.1" />` |
| `Desktop/ViewModels/SettingsViewModel.cs` | 移除 `ApplyWallpaper` / `_wallpaperBitmap` 等，改为委托 `WallpaperService`；新增卡片集合与命令 |
| `Desktop/Views/MainWindow.axaml` | 静态 `Border` → `controls:WallpaperHost` |
| `Desktop/Views/SettingsPages/SettingsAppearancePage.axaml` | **全文迁移到 FluentAvalonia `SettingsExpander` / `SettingsExpanderItem`**（含既有 4 个区块），背景壁纸区块改用折叠栏 + 卡片网格 |
| `Desktop/App.axaml` | 合入 `WallpaperStyles.axaml` |
| `Desktop/App.axaml.cs` | 启动时初始化 `WallpaperService`，窗口关闭时释放 |

### 5.3 渲染宿主设计（据 P0-2 / P0-3 修订）

**缓冲区布局**——这是本轮方案最关键的修订：

```
┌──────────────────────────────────────────────────────────────┐
│  解码线程（后台，Thread, Priority = BelowNormal）             │
│                                                              │
│   [累积解码缓冲] × 1                                         │
│      ↑ SKCodec.GetPixels(info, acc, ...)                      │
│      └─ SKCodecOptions(frameIndex: N, priorFrame: N-1)        │
│         即"把第 N 帧的差分原地应用到缓冲区上"                 │
│         缓冲区永远保持"最近一次成功合成的帧"                  │
│                    │                                          │
│                    │ memcpy（实测 0.524 ms @ 2.94 MB）        │
│                    ▼                                          │
│   [呈现缓冲] × 2  ← 轮换；每次完全覆盖写后移交              │
│                    │                                          │
└────────────────────┼──────────────────────────────────────────┘
                     │ Interlocked.Exchange 引用
                     ▼
┌──────────────────────────────────────────────────────────────┐
│  合成器线程（CompositionCustomVisualHandler.OnRender）        │
│     if (ctx.TryGetFeature<ISkiaSharpApiLeaseFeature>(out var lease)) │
│        using var api = lease.Lease();                          │
│        api.SkCanvas.DrawImage(_currentPresentImage, _destRect);│
│     RegisterForNextFrame(_plan.NextDelay);                     │
└──────────────────────────────────────────────────────────────┘
```

**为什么必须是"累积缓冲 ×1 + 呈现缓冲 ×2"而不是"三缓冲轮流写"**：

| 方案 | 能否用增量解码 | 竞态 | 结论 |
|-----|-------------|------|------|
| 双缓冲轮流写 | ❌ 待写缓冲里不是前一帧 | 有 | 否决 |
| 三缓冲轮流写 | ❌ 同上，轮换后前置条件不成立 | 有 | 否决 |
| **累积 ×1 + 呈现 ×2** | ✅ 累积缓冲始终持有当前帧 | **无** | **采用** |

呈现缓冲是**完整拷贝**，因此它的所有权完全归合成器，合成器持有多久都无所谓——竞态不是"压低概率"，而是从架构上不存在。这比"猜 Skia 会不会延迟引用"可靠得多，而代价只有每帧一次 memcpy。

其余约束不变：

- **解码在后台线程**，渲染回调内**绝不解码**
- **帧率控制**：`RegisterForNextFrame(delay)` 而非无参调用（无参会跟随 vsync 跑到 60+ fps，白烧 CPU）
- **背压**：解码耗时超过帧间隔时丢弃该帧，并按 `FramePacingPlan` 重新对齐时间轴，保证动画总时长不失真。**跳帧后 `priorFrame` 必须传"累积缓冲里实际是哪一帧"**——由于是原地增量，跳过的帧无法补算，所以传 `lastDecodedIndex` 而非 `index - 1`
- **资源释放**：窗口关闭 / 切换壁纸时先向 handler 投递 `Stop` 消息，等渲染回调确认已停止后再 `Dispose` 解码器与缓冲区，避免 UAF
- **缓冲区用非托管内存**（`Marshal.AllocHGlobal` 或 `SKBitmap.AllocPixels`）——避免 3 帧大缓冲进入 GC 堆

### 5.4 交叉淡入

两个叠加层，用 `CompositionVisual.Opacity` 做服务端动画（不占 UI 线程）：

```
Z 序（下 → 上）
  Layer A : 当前壁纸（静态或动图）
  Layer B : 新壁纸（淡入）
  内容区 / 导航栏
```

过渡期间 Layer B 的 opacity 从 0 动画到 1，结束后把 B 的内容提升为 A、B 复位。动图在过渡期间**保持播放**（不冻结），避免视觉割裂。

### 5.5 APNG 的处理策略（D7：识别但不解码）

SkiaSharp 无法解码 APNG（§3.4 P0-1），因此本轮**不做 APNG 播放**。但必须**识别**它——否则用户添加一张 APNG 会得到一张不动的图，且不知道为什么，这是最差的体验。

**处理规则**：

| 时机 | 行为 |
|-----|------|
| 添加文件时 | `BackgroundResolver` 扫到 `acTL` → 返回 `WallpaperKind.ApngUnsupported`，**不加入列表** |
| 用户提示 | 设置页顶部 `ui:InfoBar`（`Severity=Informational`）：<br>"「xxx.png」是动画 PNG（APNG），当前版本不支持播放，已跳过。另存为 GIF 或动画 WebP 后即可添加。" |
| 卡片展示 | 不生成卡片（因为没入列表） |
| 探测缓存 | 命中 `ApngUnsupported` 的指纹同样进缓存，避免每次重新扫块 |
| 多选添加 | 其余合法文件**照常入列**，APNG 单独跳过；提示条汇总（"已跳过 2 个动画 PNG"），不逐个弹窗 |

**为什么不退化为"只显示首帧"**：APNG 的 `IDAT` 首帧确实是合法 PNG，技术上完全能显示。但那属于**静默降级**——用户看到一张不动的图，既不知道是格式不支持，也不知道该怎么办。明确拒绝 + 给出转换建议，比一个"看起来成功但行为不符预期"的结果好。这个取舍记录在 D7。

**代价**：约 30 行（按 PNG 块结构扫 `IEND` 前找 `acTL`）+ 一段提示文案。**零新增依赖、零体积增量。**

> 若后续要重新支持 APNG，附录 B 的 D7 留档保留了方案 A（ImageSharp）与方案 B（自研解码器）的完整对比和实现要点，不必重新调研。

---

## 6. 性能设计（实测数据取代估算）

### 6.1 内存预算

单帧 BGRA8888 占用 = 宽 × 高 × 4（实测值）：

| 解码尺寸 | 单帧 | 累积 ×1 + 呈现 ×2 = 3 帧 | 全量缓存 60 帧（**已实测确认不会发生**） |
|---------|-----|----------------------|----------------------------------|
| 1100 × 700（默认窗口） | 2.94 MB | 8.81 MB | 176 MB |
| 1280 × 720 | 3.52 MB | 10.6 MB | 211 MB |
| 1920 × 1080 | 7.91 MB | 23.73 MB | 475 MB |
| 2560 × 1440 | 14.06 MB | 42.19 MB | 844 MB |

**"必须流式解码、禁止全量缓存"这条结论已被实测确认成立**：300 帧 1080p GIF（若全部驻留需 2373 MB）实际私有字节峰值稳定在 **17 MB**，曲线 300 帧全程平坦。Skia 的解码器**不缓存已解码帧**——`PriorFrame` 增量解码是靠"调用方在缓冲区里提供前一帧"实现的，而不是靠内部缓存。

- 解码尺寸策略：`目标长边 = min(源长边, 窗口物理长边 × 1.0, WallpaperMaxDecodeEdge)`（**只能向下夹紧**，见 §3.3）
- 窗口默认 1100×700，则常见 GIF 会先降到约 1100 长边 → 单帧 ~2.9 MB
- 全屏 1920×1080 时约 7.9 MB/帧 → 三缓冲 ~24 MB，**可接受**
- HiDPI（`RenderScaling = 2`）时物理像素翻 4 倍，必须重算解码尺寸；这是防内存爆掉的关键闸门

### 6.2 CPU（实测重写）

**每帧总成本 = 增量解码 + 呈现缓冲拷贝**（拷贝按实测 2.94 MB / 0.524 ms 线性折算）：

| 场景 | 增量解码 | 拷贝 | 合计 | @24fps 单核 | 占 12 核机器总 CPU |
|-----|---------|-----|------|-----------|-----------------|
| 1100×700 GIF | 0.19 ms | 0.52 ms | **0.71 ms** | **1.7%** | ~0.14% |
| 1080p GIF（解码降到 1100×619） | 0.30 ms | 0.48 ms | **0.78 ms** | **1.9%** | ~0.16% |
| 1080p GIF（全尺寸解码） | 0.49 ms | 1.40 ms | 1.89 ms | 4.5% | ~0.38% |
| 800×450 动画 WebP | 0.44 ms | 0.29 ms | 0.73 ms | 1.8% | ~0.15% |

**对比原估算**：原文档写"1080p @24fps 占单核 12–24%"。实测**不做增量**是 24.5%（吻合），**做增量后是 1.9%**。也就是说，把 `PriorFrame` 从"可选项"提升为**默认主路径**，是本次探针带来的最大收益。

三条缓解手段按收益排序（已重排）：

1. **`PriorFrame` 增量解码**——收益最大，约 20–27×。且已验证像素完全正确（§3.4 P0-F），可作为默认路径而非可选优化
2. **帧率上限 24fps**——大多数动图本来就是 12–25fps，上限设 24 不损失观感
3. **解码尺寸向下夹紧**——1080p 降到 1100 长边可省约 27%（10.23 → 7.42 ms）；注意只对增量前的独立解码有此收益，增量路径下解码已经不是瓶颈

外加**跳帧背压**：宁可丢帧也不让解码线程堆积。跳帧后 `priorFrame` 取"累积缓冲里实际是哪一帧"（§5.3）。

> 原先列在首位的"原生缩放省一次全尺寸缩放"已降到第 3 位——因为增量解码把解码成本压到 0.19–0.49 ms 之后，**拷贝反而成了主要成本**（0.52 ms > 0.19 ms）。如果要继续优化，方向是消除拷贝（例如让合成器直接接受累积缓冲的 SKImage，但那要重新面对 P0-2 的所有权问题），不是继续压解码。

### 6.3 GPU / 带宽

- 1280×720 @ 24fps → 3.52 MB × 24 ≈ **84 MB/s**，对现代 GPU 完全可忽略
- 风险不在带宽而在**每帧分配**：三缓冲方案下缓冲区是**复用**的，不产生每帧 `new`，无 GC 压力

### 6.4 启动开销

| 阶段 | 耗时 | 处理 |
|-----|------|------|
| 文件探测（含 `FrameCount` 遍历） | 5 – 80 ms（长 GIF 偏大） | 后台线程；结果进缓存 |
| 首帧解码 | 10 – 40 ms（实测 1080p GIF 单帧 10.23 ms） | 后台线程；窗口先显示主题底色（对应 HMCL 的 `BackgroundLoadPolicy.SHOW_FALLBACK_WHILE_LOADING` 概念） |
| 缩略图生成（设置页） | 每张 10 – 30 ms | 后台队列 + 磁盘缓存 |

**原则：`MainWindow` 的构造路径上不允许出现同步文件 IO 或解码。**

### 6.5 暂停策略（省电）

| 触发条件 | 行为 | 默认 |
|---------|------|------|
| `AnimationLevel == 0`（全局动画禁用） | 只显示首帧 | 强制 |
| `WallpaperPlayAnimated == false` | 只显示首帧 | 用户设置 |
| 窗口最小化 | 停止渲染循环 | 强制 |
| 窗口失焦 | 暂停 | `WallpaperPauseOnUnfocused`（默认关） |
| 电池供电 | 暂停 | `WallpaperPauseOnBattery`（默认开） |
| 远程桌面会话 | 暂停 | 强制 |
| 游戏进程已启动、启动器未退出 | 暂停 | 强制 |

暂停实现为向 handler 投递 `Pause` 消息并停止 `RegisterForNextFrame`，**不是降低帧率**——降低帧率仍会持续唤醒 CPU。

---

## 7. 文件体积影响

### 7.1 改造前基线（实测）

| 项目 | 体积 |
|------|------|
| `ObsMCLauncher.Desktop.dll` | **39.66 MB** |
| └ 其中 `Assets/` 全量内嵌（`AvaloniaResource`） | **39 MB** |
| └ └ `Assets/Fonts/` 四个 HarmonyOS Sans SC 字重 | **31.4 MB** |
| └ └ `logo.svg` + `logo.png` + `app_icon.icns` | 5.9 MB |
| `FluentAvalonia.dll` | 3.29 MB |
| Avalonia 系列 dll 合计 | ~6.5 MB |
| `runtimes/*/native/`（15 个 RID 全量） | ~90 MB |
| **Release 输出目录总计** | **178 MB** |

### 7.2 改造后增量

| 来源 | 增量 |
|------|------|
| `SkiaSharp` 提升为直接引用 | **0**（包内已有 3.116.1） |
| 新增 C# 代码 `SkiaAnimatedImageDecoder` 等 ~900 行 | < 0.09 MB |
| APNG 识别（扫 `acTL`，约 30 行，不解码帧数据） | **< 0.005 MB** |
| 新增 XAML / 样式 | < 0.05 MB |
| 内置示例动图（建议**不内置**） | 0 |
| **安装包净增量** | **< 0.2 MB（约 0.1%）** |

> D7 选择「只识别不解码」后，APNG 这条支线对体积的影响降到几乎为零（一段扫块逻辑），"不新增二进制"的原始前提得以维持。

### 7.3 运行时数据（不进安装包）

| 项目 | 估算 |
|-----|------|
| 缩略图缓存 `cache/wallpaper-thumbs/` | 每张 20 – 60 KB，50 张 ≈ 1 – 3 MB |
| 单张壁纸文件本体（用户自备） | 典型 GIF 1080p/5s ≈ 2 – 8 MB |
| 运行时内存增量 | 静态：0；动图：+9 – 24 MB 工作集（三缓冲，实测曲线平坦） |

### 7.4 顺带发现：真正的体积瓶颈不在这里

`Assets/Fonts/` 的 4 个字重占 **31.4 MB**，几乎等于 `Desktop.dll` 的全部体积。若对字体做**子集化**（只保留 GB2312 常用字 + ASCII + 标点），通常可压到 3 – 5 MB，**能省约 26 MB**——是本轮改造体积增量的 100 倍以上。**建议另立任务。**

---

## 8. 设置页 UI 设计（仿 ClassIsland）

### 8.1 控件选型约束（硬性）

**所有 UI 必须使用 FluentAvalonia（`ui:` 前缀）提供的控件，不自定义 `ControlTemplate`。** 允许的调整仅限 `Width` / `Height` / `CornerRadius` / `Classes` / `Margin` / `Padding` 等属性，以及项目自己的画刷资源。

已核实的 FluentAvaloniaUI **2.4.1**（本项目实际版本）可用控件：

| 控件 | 用途 | 核实方式 |
|-----|------|---------|
| `ui:SettingsExpander` | 可折叠的设置分组，带 `Header` / `Description` / `IconSource` / `IsExpanded` / `Footer` | 已在本项目 `MoreView.axaml:113` 生产使用 |
| `ui:SettingsExpanderItem` | 组内的单个设置行，带 `Content`（标题）/ `Description` / `IconSource` / `Footer`（右侧控件槽） | 已在本项目使用 56 处 |
| `ui:InfoBar` | 提示条（`Severity` / `Message` / `ActionButton`） | 已使用 10 处 |
| `ui:SymbolIcon` / `ui:SymbolIconSource` / `ui:PathIconSource` | 图标 | 已使用 56 / 5 / 21 处 |
| `ui:ItemsRepeater` + `ui:UniformGridLayout` | 卡片网格虚拟化容器 | 2.4.1 中存在（`WrapLayout` 不存在，勿用） |
| `ui:NumberBox` / `ui:FAComboBox` | 数值/下拉输入 | 已使用 3 / 1 处 |
| `ui:ProgressRing` / `ui:ContentDialog` | 加载态 / 确认弹窗 | 已使用 5 / 4 处 |

> `SettingsExpanderItem` 的**右侧控件槽是 `Footer`**，不是 `Content`——`Content` 是这一行的标题文字（本项目 `MoreView.axaml:114` 的用法即 `Content="介绍"` + `Description="..."`）。`FASettingsExpanderItem` 带 `:footerBottom` 伪类，宽度不足时 `Footer` 会自动折到标题下方。
>
> ⚠️ 该槽位结论由 `FluentAvalonia.dll`（2.4.1）的符号表与伪类名推断，**阶段 4 第一步先用 10 行最小 XAML 验证 `Content` / `Footer` 实际落位**，确认后再铺开写卡片网格。

### 8.2 参考源

**交互与视觉**参考 ClassIsland 的主题设置页（`.temp/ClassIsland/Views/SettingPages/ThemesSettingsPage.axaml`）——卡片网格 + 底部渐隐信息层 + 悬停圆形 accent 按钮。

**结构与规范**参考本项目自己的 `MoreView.axaml` / `WelcomeMigrationPageView.axaml`——`ui:SettingsExpander` 包 `ui:SettingsExpanderItem`，图标一律走 `IconSource` 子元素。

> 注意：`Views/SettingsPages/` 下现有 5 个页面（`SettingsAppearancePage` / `SettingsDownloadPage` / `SettingsGamePage` / `SettingsGeneralPage` / `SettingsHomePage`）用的是手写的 `Border Classes="settings-card"` + `Grid Classes="settings-row"`，与上述 FA 规范不一致。**本次一并迁移 `SettingsAppearancePage` 全文到 FA 控件**，避免同一页出现两套视觉语言（代价约 +0.5 天，见 §10 阶段 4）。

### 8.3 新版区块结构

**参数行全部收进折叠栏**——常显的只有「启用开关 + 卡片网格 + 两个开关」，其余 10 项参数各归入一个默认折叠的 `ui:SettingsExpander`。

```
【1】ui:SettingsExpander  Header="背景壁纸"  IconSource=SymbolIcon(Pictures)  IsExpanded=True
     ├─ Footer ─────────────────────────────────────────────► [ToggleSwitch 启用壁纸]
     └─ 展开区
        └─ 壁纸列表                        ← ui:ItemsRepeater + ui:UniformGridLayout
           ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐
           │缩略图│ │缩略图│ │缩略图│ │  ＋  │   每张 150 高，间隙 8
           │ 渐变 │ │  ✓   │ │ 渐变 │ │ 添加 │   可拖拽排序
           │遮罩层│ │      │ │遮罩层│ │      │   悬停显示 ↑ ↓ ✕
           │a.gif │ │b.png │ │c.webp│ │      │
           │GIF·24│ │PNG   │ │WEBP48│ │      │
           └──────┘ └──────┘ └──────┘ └──────┘

【2】★下拉栏  ui:SettingsExpander  Header="显示设置"
              Description="选中壁纸的呈现方式"  IsExpanded=False
     ├─ ui:SettingsExpanderItem Content="不透明度"      Footer=[Slider + 35%]
     ├─ ui:SettingsExpanderItem Content="显示方式"      Footer=[ui:FAComboBox 适应缩放]
     ├─ ui:SettingsExpanderItem Content="扩展到导航栏"  Footer=[ToggleSwitch]
     └─ ui:SettingsExpanderItem Content="导航栏透明度"  Footer=[Slider + 70%]

【3】ui:SettingsExpander  Header="动图播放"  IsExpanded=True
     ├─ Footer ─────────────────────────────────────────────► [ToggleSwitch 播放动图动画]
     └─ 展开区
        └─ ui:InfoBar  Severity=Informational      ← 仅当列表含 ≥3 张动图时显示，否则隐藏

【4】★下拉栏  ui:SettingsExpander  Header="轮播与性能"
              Description="切换节奏与动图资源占用"  IsExpanded=False
     ├─ ui:SettingsExpanderItem Content="轮播间隔"        Footer=[ui:FAComboBox 自定义…] [ui:NumberBox 90] 秒
     ├─ ui:SettingsExpanderItem Content="轮播顺序"        Footer=[ui:FAComboBox 顺序]
     ├─ ui:SettingsExpanderItem Content="切换过渡"        Footer=[Slider + 600 ms]
     ├─ ui:SettingsExpanderItem Content="帧率上限"        Footer=[ui:NumberBox 24] fps
     ├─ ui:SettingsExpanderItem Content="解码尺寸上限"    Footer=[ui:FAComboBox 1920 px]
     ├─ ui:SettingsExpanderItem Content="失焦时暂停"      Footer=[ToggleSwitch]
     └─ ui:SettingsExpanderItem Content="电池供电时暂停"  Footer=[ToggleSwitch]
```

**为什么用 4 个平级 `SettingsExpander` 而不是层层嵌套**：`FASettingsExpander` 的 `Items` 默认容器是 `FASettingsExpanderItem`，把一个 `SettingsExpander` 当作 item 塞进去会被外层容器再包一层，视觉和交互都会错位。平级排列既避免这个坑，也让每个折叠栏的主题更清晰。

### 8.4 卡片规格

网格容器用 **`ui:ItemsRepeater` + `ui:UniformGridLayout`**（FluentAvalonia 原生虚拟化容器），外侧配 Avalonia 的 `ScrollViewer`。（`ui:WrapLayout` 在 2.4.1 中不存在，勿用；`UniformGridLayout` 的 `MinItemWidth` / `MinItemHeight` / `MinRowSpacing` / `MinColumnSpacing` / `ItemsStretch` / `ItemsJustification` / `MaximumRowsOrColumns` 均已确认存在。）

| 元素 | 规格 |
|-----|------|
| 网格容器 | `ui:ItemsRepeater`，`Layout` 设为 `ui:UniformGridLayout`：`MinItemWidth=150`、`MinItemHeight=150`、`MinRowSpacing=8`、`MinColumnSpacing=8`、`ItemsStretch=Fill`、`ItemsJustification=Start` |
| 卡片尺寸 | 高 150，宽由列数自动分配（≥ 4 列时约 `(可用宽 - 3×8) / 4`） |
| 卡片容器 | `Border`，`Background=CardBackgroundBrush`，`BorderBrush=CardBorderBrush`，`BorderThickness=1`，`CornerRadius=12`，`ClipToBounds=True` |
| 缩略图 | `Image`，`Stretch=UniformToFill`；**动图只解首帧**，不播放（省 CPU） |
| 底部信息层 | 叠加 `Border`，`Background=CardBackgroundBrush`，用 `LinearGradientBrush`（0 → 0.65 全透明，1.0 全不透明）作 `OpacityMask` |
| 文件名 | 13px，`FontWeight=Medium`，`TextTrimming=CharacterEllipsis` |
| 元信息行 | 11px，`TextSecondaryBrush`，格式徽标 + 尺寸 + 帧数，例：`GIF · 1920×1080 · 24 帧` |
| 格式徽标 | 小圆角胶囊，静态用 `SubtleFillColorSecondaryBrush`；**动图用 `AccentBrush` 描边以示区分** |
| 悬停操作 | 右上角横排 3 个 `ui:SymbolIcon` 按钮（上移 / 下移 / 移除）。用 FA 原生 `Button`，仅通过 `Width=28` / `Height=28` / `CornerRadius=14` / `Padding=0` / `Classes="accent"` 调整形状，**不覆写 `ControlTemplate`**；仅在 `:pointerover` 时可见 |
| 选中态 | `BorderThickness=2`，`BorderBrush=AccentBrush`，左上角叠加 20×20 accent 圆形对勾徽标 |
| 拖拽排序 | **纳入阶段 4**：卡片支持 `DragDrop` 拖动重排；同时保留上移/下移按钮作为可访问性兜底（键盘 / 触控场景） |
| 加载态 | 缩略图未就绪时显示 `SubtleFillColorTertiaryBrush` 占位 + 居中 `ui:ProgressRing`（16×16） |
| 错误态 | 文件缺失/解码失败：缩略图位显示 `ui:SymbolIcon` + `DangerBrush`，元信息行改红字"文件不可用"，卡片仍可移除 |
| 空态 | 只有"＋ 添加"卡时，在网格下方补一行 11px 提示"支持 PNG / JPG / BMP / GIF / 动画 WebP · 不含动画 PNG（APNG）" |
| 添加卡 | `Border`，`BorderBrush=BorderBrush`，`BorderThickness=1`，虚线暂不支持 → 用 `Opacity=0.6` 的实线 + 居中 `ui:SymbolIcon`，`CornerRadius=12` |

### 8.5 交互细则

- **折叠栏状态记忆**：两个参数折叠栏的 `IsExpanded` 不写进 `LauncherConfig`（属会话级 UI 状态），改由 `SettingsViewModel` 在页面生命周期内保持；页面首次进入时均为折叠，保证默认视野干净
- **收起后的可见性**：折叠栏的 `Header` / `Description` 必须自带足够信息量（如「轮播与性能 — 切换节奏与动图资源占用」），让用户不看内容也知道里面有什么
- 添加：`OpenFileDialog` 多选，扩展名白名单 `png/apng/jpg/jpeg/webp/bmp/gif`；选择后立即后台探测。**探测到 APNG 的条目被拒收并汇总提示**（§5.5），其余文件照常入列
- 动图误入提示：若用户一次添加 ≥ 3 张动图，【3】区块的 `ui:InfoBar`（`Severity=Informational`）显示："已添加多张动图，同时播放会占用较多资源，建议配合轮播使用。"
- 单张动图超大提示：探测发现 `FrameCount > 300` 或解码后单帧 > 16 MB 时，卡片元信息行追加警示图标 + ToolTip 说明
- 移除当前生效项：自动切到列表首项；列表清空则壁纸整体失效（`WallpaperEnabled` 不自动置假，UI 显示空态）
- 轮播间隔：`ui:FAComboBox` 提供预设档位（关闭 / 30 秒 / 1 分钟 / 5 分钟 / 15 分钟 / 每次启动 / 自定义…）。选中「自定义…」时，同一 `Footer` 内联显示 `ui:NumberBox`（`Minimum=5`、`Maximum=86400`、`SpinButtonPlacementMode=Inline`、`SmallChange=5`），写回 `WallpaperSlideIntervalSeconds`；重新进入设置页时若当前值不在预设档位中，自动定位到「自定义…」并回填数值
- 拖拽排序：卡片支持按住拖动重排（Avalonia `DragDrop`），拖拽结束后写回 `WallpaperItems` 顺序并立即保存；上移/下移按钮保留作为键盘与触控场景的兜底
- 不透明度/显示方式/扩展到导航栏 三项**对静态与动图统一生效**，语义不变

### 8.6 新增样式（`Styles/WallpaperStyles.axaml`）

因为改用 FA 原生控件，**样式文件只保留卡片网格这一处自定义**；所有参数行的外观由 FluentAvalonia 主题提供，不再需要 `settings-card` / `settings-row` / `settings-divider` 这套手写样式。

```
Border.wallpaper-card                    卡片容器
Border.wallpaper-card:pointerover        悬停：BorderBrush → AccentBrush（0.5 透明）
Border.wallpaper-card.selected           选中：BorderThickness=2 + AccentBrush
Button.wallpaper-card-action             圆形小按钮（只调 Width/Height/CornerRadius/Padding）
Panel.wallpaper-card-actions             右上角操作组，默认 Opacity=0
Border.wallpaper-card-badge              格式徽标胶囊
Border.wallpaper-card-add                添加卡
```

### 8.7 XAML 骨架（关键片段）

```xml
<!-- 【1】背景壁纸：Footer 放开关，展开区放卡片网格 -->
<ui:SettingsExpander Header="背景壁纸"
                     Description="为启动器主界面设置本地图片或动图"
                     IsExpanded="True">
  <ui:SettingsExpander.IconSource>
    <ui:SymbolIconSource Symbol="Pictures" />
  </ui:SettingsExpander.IconSource>
  <ui:SettingsExpander.Footer>
    <ToggleSwitch IsChecked="{Binding WallpaperEnabled, Mode=TwoWay}"
                  OnContent="" OffContent="" />
  </ui:SettingsExpander.Footer>

  <ui:ItemsRepeater ItemsSource="{Binding WallpaperItems}">
    <ui:ItemsRepeater.Layout>
      <ui:UniformGridLayout MinItemWidth="150" MinItemHeight="150"
                            MinRowSpacing="8" MinColumnSpacing="8"
                            ItemsStretch="Fill" ItemsJustification="Start" />
    </ui:ItemsRepeater.Layout>
    <ui:ItemsRepeater.ItemTemplate>
      <DataTemplate x:DataType="vm:WallpaperItemViewModel">
        <Border Classes="wallpaper-card" Classes.selected="{Binding IsSelected}">
          <Panel>
            <Image Source="{Binding Thumbnail}" Stretch="UniformToFill" />
            <Border Classes="wallpaper-card-caption">
              <StackPanel>
                <TextBlock Text="{Binding FileName}" FontSize="13" FontWeight="Medium"
                           TextTrimming="CharacterEllipsis" />
                <StackPanel Orientation="Horizontal" Spacing="4">
                  <Border Classes="wallpaper-card-badge">
                    <TextBlock Text="{Binding KindLabel}" FontSize="11" />
                  </Border>
                  <TextBlock Text="{Binding MetaLabel}" FontSize="11"
                             Foreground="{DynamicResource TextSecondaryBrush}" />
                </StackPanel>
              </StackPanel>
            </Border>
          </Panel>
        </Border>
      </DataTemplate>
    </ui:ItemsRepeater.ItemTemplate>
  </ui:ItemsRepeater>
</ui:SettingsExpander>

<!-- 【2】下拉栏：显示设置 —— 参数行全部收进 Footer 槽 -->
<ui:SettingsExpander Header="显示设置"
                     Description="选中壁纸的呈现方式"
                     IsExpanded="False">
  <ui:SettingsExpanderItem Content="不透明度" Description="壁纸的透明程度">
    <ui:SettingsExpanderItem.Footer>
      <StackPanel Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
        <Slider Width="150" Minimum="0" Maximum="1"
                Value="{Binding WallpaperOpacity, Mode=TwoWay}" />
        <TextBlock Text="{Binding WallpaperOpacity, StringFormat={}{0:P0}}"
                   FontSize="12" VerticalAlignment="Center"
                   Foreground="{DynamicResource TextSecondaryBrush}" />
      </StackPanel>
    </ui:SettingsExpanderItem.Footer>
  </ui:SettingsExpanderItem>

  <ui:SettingsExpanderItem Content="显示方式" Description="壁纸的拉伸方式">
    <ui:SettingsExpanderItem.Footer>
      <ui:FAComboBox Width="140" SelectedIndex="{Binding WallpaperStretch, Mode=TwoWay}">
        <ui:FAComboBoxItem Content="拉伸填充" />
        <ui:FAComboBoxItem Content="适应缩放" />
        <ui:FAComboBoxItem Content="裁剪填充" />
        <ui:FAComboBoxItem Content="原始大小" />
      </ui:FAComboBox>
    </ui:SettingsExpanderItem.Footer>
  </ui:SettingsExpanderItem>

  <ui:SettingsExpanderItem Content="扩展到导航栏" Description="将壁纸延伸到左侧导航栏区域">
    <ui:SettingsExpanderItem.Footer>
      <ToggleSwitch IsChecked="{Binding WallpaperExtendToNav, Mode=TwoWay}"
                    OnContent="" OffContent="" />
    </ui:SettingsExpanderItem.Footer>
  </ui:SettingsExpanderItem>
</ui:SettingsExpander>
```

两个实现注意点：

1. **不要给网格再套一层 `ScrollViewer`**——设置页外层已有滚动容器，嵌套滚动会互相抢滚轮事件。
2. `Classes.selected="{Binding IsSelected}"` 是 Avalonia 支持的伪类绑定写法，选中态无需自定义 `ControlTemplate`。

---

## 9. 其他问题与风险

### 9.1 安全与健壮性

| 风险 | 说明 | 缓解 |
|-----|------|------|
| **解码炸弹** | 一个 4K × 5000 帧的 GIF 足以打爆内存与 CPU | 三重闸门：源尺寸上限（长边 ≤ 8192）、`FrameCount` 上限（≤ 2000）、探测后按公式预估单帧内存，超限则拒绝并提示 |
| APNG 识别逻辑被畸形文件误导 | 只做"扫块 + 读 `acTL`"，不碰帧数据；但块长度字段若被构造成异常值，仍可能越界或死循环 | **只在 PNG 签名完全匹配后才进入扫块**；每个块长度做上界校验并防整数溢出；扫不到 `IEND` 立即中止；全程 `Span<byte>` 边界检查；任何异常降级为 `Unknown`（按普通静态图处理），不向调用方抛出 |
| 畸形文件导致原生崩溃 | `SKCodec` 是原生代码，malformed 文件理论上可能触发崩溃 | 用 `SKCodec.Create` 的 `SKCodecResult` 重载做结果校验；解码包在 try/catch；探测与解码都在后台线程 |
| 路径校验 | 现有约束：本地文件、扩展名白名单 | 沿用；**本轮不引入网络 URL**，规避 SSRF / 版权 / 缓存淘汰问题 |
| 文件被外部移动/删除 | 启动后壁纸突然消失 | 每次应用前 `File.Exists`；失败则跳过该条目并置为错误态，不弹阻断性对话框 |
| 原生库线程安全 | `SKCodec` / `SKBitmap` 可跨线程使用，但**同一实例不能并发读写** | 每个解码器实例绑定单一解码线程；缓冲区按 §5.3 的"累积缓冲专属解码线程 + 呈现缓冲拷贝移交"协议 |
| `PriorFrame` 前置条件被破坏 | 若缓冲区内容与传入的 `priorFrame` 不符，解出的是错帧（不报错） | 累积缓冲的写入者唯一（解码线程）；`priorFrame` 一律取"上一次成功解码的帧序号"而非 `index - 1`；单测覆盖"跳帧后背压"路径 |

### 9.2 工程与 CI

| 风险 | 说明 | 缓解 |
|-----|------|------|
| 测试项目需要 SkiaSharp 原生库 | 解码器若放 `Core`，`ObsMCLauncher.Core.Tests` 需要能加载原生库 | `SkiaSharp` 包自带 `runtimes/`，xunit 进程可直接加载；若遇加载失败，在测试 csproj 显式加 `SkiaSharp` 引用 |
| AGENTS.md 约束"Core 无 UI 依赖" | `Core` 已在引用 `Avalonia`；再加 `SkiaSharp` 属图形依赖而非 UI 依赖 | 在 AGENTS.md 中补一句说明，避免后续误解 |
| **`SkiaSharp.NativeAssets.Linux` 版本不匹配** | 解析为 2.88.9，托管库为 3.116.1（§2.3） | 与本方案正交。建议显式引用 3.116.1 后在 Linux RID 上跑一次产物校验；**另立任务** |
| 7 RID CI 矩阵 / Velopack 打包 | 无新增原生库 → 不受影响 | 阶段 5 各跑一次产物校验 |
| IL trimming | 当前未启用 trimming，SkiaSharp 反射路径未受影响 | 若未来启用，需为 SkiaSharp 添加 root descriptor |
| 测试数据 | 需要 GIF / 动画 WebP / 静态图样本；另需 APNG 样本用于验证拒绝路径（A13） | **探针已产出成套素材**，见 §C.3，可直接拷进测试目录。`.temp/probe/assets/anim_dirty.png` 与 `anim_full.png` 即合法 APNG |

### 9.3 交互与体验

| 风险 | 说明 | 缓解 |
|-----|------|------|
| 设置页缩略图密集解码导致页面卡顿 | 20 张 4K 图同时解缩略图 | 后台队列 + 并发上限 2 + 磁盘缓存 + 视口内优先 |
| 首次切换到大动图有明显等待 | 首帧解码实测 1080p GIF 10.23 ms，4K 源更久 | 保持上一层内容直到新层就绪，再启动交叉淡入 |
| 动图与内容区可读性冲突 | 动图默认不透明度 0.35，但仍可能干扰阅读 | 沿用不透明度滑块；在提示中说明"动图建议配合更低不透明度" |
| 用户把 `.png` 加进来却是静态图 | 期望落空 | 探测结果即卡片元信息里的格式徽标，所见即所得 |
| **用户添加 APNG** | 用户期望动起来，实际不支持 | **添加时即拒收并给出可操作提示**（§5.5），不静默退化为首帧；文案明确建议另存为 GIF / 动画 WebP |
| 减少动效偏好 | 部分用户晕动/省电 | `AnimationLevel == 0` 与"播放动图动画"开关双重覆盖 |

### 9.4 授权

- **SkiaSharp**：MIT（原生 Skia 为 BSD-3）→ **无新增授权负担**
- **若采纳 D7 方案 A（ImageSharp）**：Six Labors Split License —— 年收入低于阈值的组织可免费使用，但 **v4 起对直接依赖强制编译期许可证密钥**，需要确认本项目的使用场景是否满足；这是一条新的合规审查项
- 用户自备动图的版权归用户，仅本地读取与展示，不涉及再分发
- 对照：视频路线需引入 LibVLCSharp（LGPL-2.1）或 FFmpeg（构建选项含 GPL 时需开源）→ 这是排除视频路线的次要但真实的理由

---

## 10. 实施计划（据探针结论修订）

| 阶段 | 内容 | 前置 | 产出 |
|-----|------|------|------|
| **0. 探针验证** | ✅ **已完成**。GIF / APNG / 动画 WebP 支持性；`GetScaledDimensions` 与原生缩放正确性；`PriorFrame` 增量解码的加速比、正确性与内存影响；缓冲区所有权语义；1080p 单帧耗时 | — | 见 §3.4 与附录 C；**结论：APNG 不可用，D7 已决策放弃** |
| **0.5 APNG 拒绝策略（已决策，合并入阶段 1）** | D7 结论：放弃 APNG 播放，保留识别。落地内容很小——`BackgroundResolver` 的 `acTL` 扫描 + 添加时拒收 + `InfoBar` 文案 + 畸形 PNG 块的单测 | 阶段 0 | APNG 被识别并明确提示，不静默降级 |
| **1. 模型与解码器** | `WallpaperItem` / `WallpaperKind` / `BackgroundResolver` / `AnimatedImageInfo` / `IAnimatedImageDecoder` / `SkiaAnimatedImageDecoder`（含 `PriorFrame` 主路径）/ `FramePacingPlan` / `WallpaperThumbnailer`；config 字段变更（删除 `WallpaperPath`，**不做迁移**） | 阶段 0 | Core 单测全绿；`dotnet test` 通过 |
| **2. 渲染宿主** | `AnimatedImagePresenter`（累积缓冲 ×1 + 呈现缓冲 ×2 / 帧率上限 / 跳帧背压 / Pause-Resume）/ `WallpaperHost` / `WallpaperService` / `PowerStatusMonitor`；`MainWindow.axaml` 改造 | 阶段 1 | 单张动图可播放，静态路径零回归 |
| **3. 轮播** | `WallpaperRotationScheduler`（间隔 / 顺序 / 随机 / 动图播完再切）+ 交叉淡入 | 阶段 2 | 多图轮播可用，边界场景通过 |
| **4. 设置页 UI** | ① 用 10 行最小 XAML 验证 `SettingsExpanderItem` 的 `Content` / `Footer` 实际落位；② `SettingsAppearancePage` 全文迁移到 FA 规范；③ 卡片网格（`ui:ItemsRepeater` + `ui:UniformGridLayout`）、拖拽排序、悬停操作、选中态、徽标、空态/加载态/错误态；④ 两个折叠栏；⑤ `WallpaperStyles.axaml` | 阶段 3 | UI 走查通过 |
| **5. 打磨与验收** | 最小化/失焦/电池/DPI 变更/远程桌面验证；性能基准记录；更新 AGENTS.md、FEATURES.md | 阶段 4 | 验收标准逐条打勾 |

**关键路径**：阶段 0（✅）→ 1 → 2 → 3 → 4 → 5。**D7 已决策，无阻塞项，可直接开工阶段 1。**

阶段 0 的三个 P0 风险已全部关闭：P0-1 结论是"APNG 不可用"，已由 D7 决策为「识别但不解码」；P0-2 转化为一条确定的架构约束（累积 ×1 + 双呈现 ×2）；P0-3 拿到了真实数字并发现增量解码这个大杠杆。

---

## 11. 验收标准（据实测修订）

| 编号 | 指标 | 目标值 | 依据 |
|-----|------|--------|------|
| A1 | 静态壁纸 CPU 回归 | 与改造前偏差 ≤ 0.5% | 静态路径不经过解码器 |
| A2 | 1100×700 GIF @24fps 单核 CPU | **≤ 3%**（原定 ≤ 8%） | 实测 1.7%，留余量 |
| A3 | 1920×1080 GIF @24fps 单核 CPU | **≤ 6%**（原定 ≤ 24%） | 实测 1.9%（解码降到 1100 长边）/ 4.5%（全尺寸） |
| A4 | 动图运行时工作集增量 | ≤ 40 MB（1100×700）/ ≤ 80 MB（1080p 全屏） | 三缓冲 8.81 / 23.73 MB + 解码器开销 |
| A5 | 窗口最小化后 5 秒内 CPU | < 0.5% | — |
| A6 | 安装包增量 | ≤ 0.5 MB | §7.2（预估 < 0.2 MB） |
| A7 | 切换壁纸的主线程阻塞 | 无单次 > 100 ms | — |
| A8 | 连续切换 100 次工作集增长 | ≤ 10 MB（无泄漏） | — |
| A9 | 动图格式支持 | GIF / 动画 WebP 各至少 1 例通过 | §3.4 P0-1 |
| A10 | 异常输入 | 损坏文件、0 字节文件、超限帧数文件均不崩溃且给出提示 | §9.1 |
| **A11** | **长动图内存不随帧数增长** | 300 帧 1080p 播放全程工作集 ≤ 基线 + 40 MB | 实测私有字节峰值 17 MB 平坦（§3.4 P0-F） |
| **A12** | **增量解码像素正确性** | 与独立解码逐帧哈希一致 | 实测 300/300、60/60、40/40 全通过 |
| **A13** | **APNG 拒绝路径** | 添加 APNG 时被拒收、提示文案正确、列表未被污染、同批其他文件正常入列 | §5.5 |

---

## 附录 A：为什么排除视频背景

| 维度 | 视频路线 | 动图路线（本方案） |
|------|---------|------------------|
| Avalonia 支持 | **无内置播放器**，需引入 LibVLCSharp.Avalonia 或 FFmpeg.AutoGen | 无内置，但 `SKCodec` 已在包内 |
| 新增二进制 | LibVLCSharp 需 `VideoLAN.LibVLC.Windows/Mac` 原生包，未裁剪约 **+40 ~ 150 MB / 平台**；Linux 还需系统安装 libvlc | **0** |
| 授权 | VLC LGPL-2.1；FFmpeg 视构建选项可能为 GPL | MIT / BSD |
| 跨平台 | Windows/Mac 有官方原生包，Linux 依赖系统包；Wayland 下 surface 处理有已知坑 | 纯托管 + 现有原生库，7 RID 无差异 |
| 常驻 CPU | 软件解码 1080p 显著高于 GIF；硬解路径各平台差异大 | 实测 1.9% 单核（含增量解码），可控 |
| 效果收益 | 观感最强，但**与"启动器"产品调性不符** | 与产品调性一致 |

**结论**：视频路线在体积（+40–150 MB）、授权、跨平台适配三方面成本都远超收益，且用户本人也认为"不贴合软件方向"。动图 + 轮播是性价比最优解。

---

## 附录 B：决策记录

| 编号 | 议题 | 结论 | 落地位置 |
|-----|------|------|---------|
| D1 | 轮播间隔档位 | 档位取 **关闭 / 30 秒 / 1 分钟 / 5 分钟 / 15 分钟 / 每次启动 / 自定义…**；**必须支持用户自定义任意秒数** | §4.1 `WallpaperSlideIntervalSeconds`、§8.3、§8.5 |
| D2 | 参数行如何收纳 | **10 项参数行全部收进两个折叠栏**（`ui:SettingsExpander`，默认折叠）；常显的只有开关 + 卡片网格。不做"极简模式"总开关 | §8.3、§8.5 |
| D3 | 拖拽排序 | **纳入阶段 4**，与上移/下移按钮并存 | §8.4、§8.5、§10 |
| D4 | 内置示例动图 | **不提供**，仅在空态给出格式提示文案 | §8.4 空态、§7.2 |
| D5 | 配置文件迁移 | 当前为测试版，**不做任何迁移**，允许破坏性变更（删除 `WallpaperPath`） | §4.1、§4.3 |
| D6 | UI 控件规范 | **所有 UI 必须使用 FluentAvalonia 控件**（`ui:` 前缀），不自定义 `ControlTemplate`；`SettingsAppearancePage` 全文迁移到 FA 规范 | §8.1、§8.6、§10 |
| **D7** | **APNG 如何处理（探针暴露）** | **放弃播放、保留识别**。`BackgroundResolver` 扫 `acTL`，命中则添加时拒收并给出可操作提示；不引入新依赖、不自研解码器 | §3.4 P0-1、§5.5、§10 阶段 0.5 |

### D7 决策留档

**结论：采取方案 C —— 放弃 APNG 播放，但保留 APNG 识别。** 用户判断"为了 APNG 单开一条解码链路太麻烦"，接受不支持。

落地形态不是"完全不处理"，而是 §5.5 的**识别 + 拒收 + 可操作提示**——约 30 行扫块代码，零新增依赖、零体积增量。这样用户拿到的是"明确告知"，而不是"图不动但没人说为什么"。

**为什么否掉另两条路**（留档，避免将来重复调研）：

| 维度 | 方案 A：引入 ImageSharp | 方案 B：自研 APNG 解码器 | 方案 C：**放弃播放**（采纳） |
|-----|----------------------|---------------------|-------------------------|
| 支持度 | 3.1+ 完整支持 APNG 解码 | 需覆盖全部颜色类型/位深组合 | 仅识别，不播放 |
| 安装包增量 | +1.5 ~ 2 MB | < 0.05 MB | **< 0.005 MB** |
| 新依赖 | 是 | 否 | **否** |
| 授权 | Six Labors Split License，v4 起强制编译期许可证密钥 | 自有代码 | 无影响 |
| 工作量 | 约 1 天 | **约 3–4 天**（含畸形样本防护） | **约半天** |
| 内存模型 | `Image<T>` 需整图驻留，**多帧 = 多帧内存，与 §6.1 的流式结论直接冲突** | 可把帧直接合成进累积缓冲 | 不涉及 |
| 被否原因 | 体积放大 10 倍以上 + 许可证摩擦 + 内存模型冲突 | **工作量最大**，且引入一份全新的二进制格式攻击面 | — |

**若将来要重新支持 APNG**，这里是实现要点，不必重新调研：

```
PNG 签名
IHDR   画布尺寸 / 位深 / 颜色类型 / 交错方式
acTL   总帧数 num_frames、循环次数 num_plays（0 = 无限）
[可选] 若第一个 fcTL 出现在 IDAT 之前 → IDAT 是动画的第 0 帧
       否则 IDAT 只是"静态降级图"，动画从 fdAT 开始
fcTL+IDAT  第 0 帧（若参与动画）
fcTL+fdAT  第 1..N-1 帧，每帧带 seq / 尺寸 / 偏移 / delay_num/den / dispose_op / blend_op
IEND
```

1. 扫块，收集 `acTL` 与每帧 `fcTL`，把 `IDAT`/`fdAT` 的压缩数据按帧归组
2. 每帧用 `System.IO.Compression.ZLibStream` 解压 → 得到"过滤后的扫描行"
3. 按 PNG 规范**反滤波**（None / Sub / Up / Average / Paeth 五种）
4. 按 `blend_op` 合成到画布（`OP_SOURCE` 覆盖，`OP_OVER` 做 alpha 合成）
5. 按 `dispose_op` 处理帧后状态（`NONE` / `BACKGROUND` / `PREVIOUS`）

需要覆盖的颜色类型：真彩+Alpha（6）、真彩（2）、索引（3）、灰度（0）、灰度+Alpha（4）；位深 8/16（16 位降 8 位）以及索引/灰度下的 1/2/4 位。**Adam7 交错建议直接拒绝**——APNG 极少交错，收益不抵复杂度。

> 一个有利细节：因为要自己持有"当前合成画布"，自研 APNG 解码器与 §5.3 的**累积缓冲是同一个东西**——帧数据直接合成进累积缓冲，不需要中间画布，内存开销为零增量。这也是方案 B 优于方案 A 的地方。

### D2 收纳方式说明（留档）

原先提的"极简模式"是一个全局开关，一键隐藏全部参数行。改为**用折叠栏收纳**后，同一个目标用更小的成本达成，且没有副作用：

| 区块 | 常显内容 | 折叠内容 |
|-----|---------|---------|
| 【1】背景壁纸 | 启用开关（Footer）+ 卡片网格 | — |
| 【2】显示设置（★折叠栏） | — | 不透明度、显示方式、扩展到导航栏、导航栏透明度 |
| 【3】动图播放 | 播放动图动画开关（Footer）+ 条件性 `InfoBar` | — |
| 【4】轮播与性能（★折叠栏） | — | 轮播间隔、轮播顺序、切换过渡、帧率上限、解码尺寸上限、失焦暂停、电池暂停 |

相比"极简模式"的好处：参数没有消失，只是被收起来——用户遇到"风扇转起来了"时仍能找到「帧率上限」「解码尺寸上限」去调；也不需要维护一份"该显示哪些项"的开关逻辑。

### D6 补充：为什么要求在 `SettingsAppearancePage` 全文迁移

项目内目前有两套设置页视觉语言：

- `MoreView.axaml`、`WelcomeMigrationPageView.axaml` → FluentAvalonia `SettingsExpander` / `SettingsExpanderItem`（12 处区块、56 处行）
- `Views/SettingsPages/` 下 5 个页面 → 手写 `Border Classes="settings-card"` + `Grid Classes="settings-row"`（14 处卡片）

本次壁纸区块必然要改写，如果只改壁纸部分，同一页会出现"折叠栏 + 手写卡片"混排。因此把 `SettingsAppearancePage` 的另外 4 个既有区块（字体、主题色、动效、密度）一并迁移，让整页统一到 FA 规范。其余 4 个设置页不在本次范围内，可作为后续独立任务。

---

## 附录 C：阶段 0 探针原始数据

### C.1 探针工程

| 路径 | 说明 |
|-----|------|
| `.temp/probe/gen_assets.py` | 生成测试素材（GIF / APNG / 动画 WebP / 静态图），两类帧序列：`dirty`（小圆点移动，帧间差异极小）与 `full`（背景整体滚动，近乎全帧变化） |
| `.temp/probe/gen_long.py` | 生成 300 帧长素材，用于验证内存是否随帧数增长 |
| `.temp/probe/SkiaProbe/` | .NET 8 控制台工程，引用 `SkiaSharp 3.116.1` + `SkiaSharp.NativeAssets.Win32 3.116.1` |
| `.temp/probe/assets/` | 生成的素材，可直接拷进 `tests/` 作为回归数据 |

`.temp/` 不在任何 `.csproj` 内，不参与编译，不影响主构建。

运行环境：Windows x64，12 逻辑核，.NET 8.0.20，SkiaSharp 3.116.1（托管程序集版本号 3.116.0.0）。

### C.2 素材清单

| 文件 | 尺寸 | 帧数 | 体积 | 脏矩形均值 |
|-----|------|-----|------|----------|
| `anim_dirty_1100x700.gif` | 1100×700 | 90 | 583.9 KB | 21.8% |
| `anim_full_1100x700.gif` | 1100×700 | 60 | 358.9 KB | 22.1% |
| `anim_1080p.gif` | 1920×1080 | 60 | 784.4 KB | 22.6% |
| `anim_1080p_full.gif` | 1920×1080 | 40 | 500.1 KB | 23.0% |
| `long_1100x700.gif` | 1100×700 | 300 | 2.1 MB | — |
| `long_1080p.gif` | 1920×1080 | 300 | 3.5 MB | — |
| `anim_dirty.png`（**APNG**） | 800×450 | 48 | 103.0 KB | — |
| `anim_full.png`（**APNG**） | 800×450 | 32 | 71.1 KB | — |
| `anim_dirty.webp` | 800×450 | 40 | 55.2 KB | 36.3% |
| `long.webp` | 800×450 | 300 | 0.4 MB | — |
| `static.png` / `static.webp` | 800×450 | 1 | 3.2 KB | — |
| `tiny.gif` | 64×64 | 8 | 8.7 KB | 18.2% |

APNG 素材经独立校验：`acTL` 声明 48 / 32 帧，`fcTL`/`fdAT` 块齐全，Pillow 读取 `n_frames` 为 48 / 32、`is_animated = True`——**确认是合法 APNG，问题在 SkiaSharp 一侧**。

D7 决策为「识别但不解码」后，这两份 APNG 素材的用途转为**验收 A13（拒绝路径）**：直接拷进测试目录，验证 `BackgroundResolver` 能正确扫出 `acTL` 并返回 `ApngUnsupported`。

### C.3 复现方式

```bash
# 1. 生成素材
python gen_assets.py && python gen_long.py

# 2. 运行探针（输出 A–G 共 7 段）
cd SkiaProbe && dotnet run -c Release
```

### C.4 探针过程中的两个方法论教训（留档）

1. **不要用 `strings` + `grep` 查二进制符号**。本环境没有 `strings`；`grep -a` 对 C++ 符号名做正对照（`grep -c 'SKCodec'`）返回 0，因为 SkiaSharp 原生库导出的是 `sk_codec_*` 小写 C ABI。这产生了两次假阴性。**改用行为测试 + 官方文档交叉印证**。
2. **性能测量必须区分"独立解码"与"增量解码"**。只测前者会得到 24.5% 的悲观数字并据此做出错误的架构取舍（例如为了省 CPU 而放弃某些格式）。两类都要测，且要验证增量路径的像素正确性。
