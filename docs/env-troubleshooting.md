# 环境踩坑记录：GitHub 访问、代理与 NuGet

记录在配置本机开发环境时踩到的坑，以及**最终被实测证实的结论**。
每条都附上验证方法，方便以后复现或推翻。

---

## 一、NuGet：Visual Studio 残留的 fallback 目录导致构建直接失败

### 现象

`dotnet build` 报错，无法编译：

```
error MSB4018: "ResolvePackageAssets" 任务意外失败
NuGet.Packaging.Core.PackagingException: 无法找到回退包文件夹"F:\C++\工具\NuGetPackages"
```

### 根因

机器级别的 NuGet 配置里有一条 Visual Studio 安装时写入的 fallback 目录，
指向**已被删除**的路径：

```
C:\Program Files (x86)\NuGet\Config\Microsoft.VisualStudio.FallbackLocation.config
  <add key="Microsoft Visual Studio Fallback Folder" value="F:\C++\工具\NuGetPackages" />
```

这个路径不存在，而 NuGet 在还原时要求 fallback 目录**必须存在**，于是直接抛异常。

### 尝试过但**无效**的方案

在 `.csproj` 里设置这两个属性**没有用**：

```xml
<RestoreFallbackFolders></RestoreFallbackFolders>
<RestoreAdditionalProjectFallbackFolders></RestoreAdditionalProjectFallbackFolders>
```

原因：`ResolvePackageAssets` 读的是 `obj/project.assets.json` 里**已经固化**的 `packageFolders`，
属性只在 restore 阶段起作用，而 restore 产出的缓存没被这条设置影响。

### 有效方案：项目级 `nuget.config` 用 `<clear />`

在项目目录放一个 `nuget.config`：

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <fallbackPackageFolders>
    <clear />
  </fallbackPackageFolders>
</configuration>
```

`<clear />` 会清掉从上层继承来的 fallback 目录。

**注意**：如果同时要发布 RID 专用产物（`-r win-x64`），需要恢复默认包源，
因为运行时包要从 nuget.org 下载：

```xml
<configuration>
  <fallbackPackageFolders>
    <clear />
  </fallbackPackageFolders>
  <!-- 不写 packageSources，保持默认 nuget.org -->
</configuration>
```

### 关键点

**没有改动用户全局配置。** 全局那份残留配置留着不影响别人，项目内覆盖即可。
改全局配置属于"污染用户环境"，能避免就避免。

---

## 二、`gh` 和 `git` 的代理需求完全不同

这是本次踩得最深的一个坑，**连续两次判断错误**。

### 结论先行

| 工具 | 是否需要显式代理 | 实测成功率 |
| --- | --- | --- |
| `gh`（GitHub CLI） | **不需要**，走系统代理 / TUN 即可 | 6/6 |
| `gh` + 显式 `HTTPS_PROXY` | ❌ **有害**，反而变差 | 1/4 |
| `git`（HTTPS 推送） | **必须显式配置** | 无代理 0/3 · 有代理 3/3 |

**同一台机器、同一个时刻、同一个 GitHub，两个工具的行为完全相反。**

### 现象与排查过程

#### 阶段 1：`gh auth login --web` 失败

```
failed to authenticate via web browser:
Post "https://github.com/login/oauth/access_token": dial tcp 20.205.243.166:443: connectex: ...
```

**错误判断 ①**：以为设备码流程的网络出问题，让用户换了代理节点。换完仍然失败。

#### 阶段 2：误判 GraphQL 被节点拒绝

用 PowerShell 裸请求测试 GraphQL 接口：

```powershell
Invoke-WebRequest 'https://api.github.com/graphql' -Method Post -Body '{"query":"{viewer{login}}"}' ...
# 结果：HTTP 403
```

**错误判断 ②**：认定"当前代理出口 IP 被 GitHub 针对 graphql 拒绝"，
于是给出了"换节点"和"改用令牌登录"两条建议。其中"换节点"是错的方向。

#### 阶段 3：用目标程序自己带 debug 跑，真相浮现

关键一步——**让 `gh` 自己输出网络日志**：

```powershell
$env:GH_DEBUG = "api"
gh api /user    # 带一个假令牌，只为触发网络请求
```

输出：

```
* Request to https://api.github.com/user
> GET /user HTTP/1.1
< HTTP/2.0 401 Unauthorized          ← 网络是通的！
```

再测 GraphQL：

```
* Request to https://api.github.com/graphql
> POST /graphql HTTP/1.1
< HTTP/2.0 401 Unauthorized          ← 也是通的！
```

**`gh` 的网络完全正常。** 那 403 是 PowerShell **裸请求**（未认证 GraphQL 限流）造成的假象，
与 `gh` 毫无关系。

#### 阶段 4：定位真实原因——`gh` 没拿到代理

回看用户的终端截图才发现：用户设了 `$env:HTTPS_PROXY = "..."`，
但 `gh` 报的错是连接 `api.github.com` 直连超时——说明**它根本没走代理**。
用户那个窗口的执行顺序有问题（赋值那行没生效就执行了登录）。

#### 阶段 5：又一个反向错误——代理设了反而更差

为了"确保走代理"，我把 `HTTPS_PROXY` 写进了**用户级环境变量**。
结果稳定性反而崩了：

```
不设 HTTPS_PROXY        : 成功 4/4   ✅
设 HTTPS_PROXY=Clash    : 成功 1/4   ❌
```

**根因**：Clash 开了 TUN / 系统代理，流量本来就被接管了。
再叠加一层显式 HTTP 代理，导致流量绕两圈，握手大量失败。

**最终配置**：

| 项 | 值 |
| --- | --- |
| `gh` | 不设任何代理环境变量，走系统代理 |
| `git` | `git config --global http.proxy http://127.0.0.1:7890` |
| 用户级 `HTTP_PROXY` / `HTTPS_PROXY` | 已清除 |

### 为什么两个工具行为相反

- **`gh` 是 Go 写的**，`net/http` 的 `ProxyFromEnvironment` 会读 `HTTPS_PROXY`，
  但**不读** Windows 的 IE 代理设置（注册表 `Internet Settings`）。
  然而它有另一条路：Go 的 DNS 解析 + TLS 走的是系统网络栈，
  在 Clash TUN 模式下，流量在**网络层**就被接管了，所以不需要应用层代理。
- **`git` 用 libcurl**，libcurl 的代理支持是显式的：
  要么读环境变量，要么读 `http.proxy` 配置。
  TUN 模式下 git 的 TLS 连接反而容易握手失败（Empty reply / 超时）。

### 排查方法论（真正的教训）

> **不要用 A 工具的现象去推断 B 工具的行为。**
> 尤其是代理这种中间层，每个工具的实现路径都不同。

正确顺序：

1. **先让目标程序自己说话** —— `GH_DEBUG=api`、`git -v`、`curl -v` 这类原生调试输出
2. **再做受控对照实验** —— 同一操作、只改一个变量、跑多次取成功率
   （本次"不设代理 4/4 vs 设代理 1/4"就是这么定论的）
3. **旁路观测只能当线索，不能当结论** —— PowerShell 的 `Invoke-WebRequest` 结果
   与 `gh` 的实际行为无关

---

## 三、附带发现：非 DPI 感知的截图会"骗人"

做界面验收时用 PowerShell 截图，测出窗口是 `747x480`，看着像被裁切。
但用程序自己的 `--env` 模式打印出来是：

```
最终 Bounds : {X=720,Y=324,Width=1120,Height=720}
```

真实尺寸是 **1120x720**。

**原因**：截图进程**不是 DPI 感知**的，Windows 会对它做坐标虚拟化
（显示器 2560x1440 @150% 缩放，虚拟化后得到 1707x960 的逻辑坐标系，
窗口在其中被报成 747x480）。

**解决**：截图前先声明 DPI 感知：

```csharp
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
SetProcessDpiAwarenessContext(new IntPtr(-4));   // -4 = PER_MONITOR_AWARE_V2
```

这条同样印证了上面的方法论：**用被观测对象自己的说法，而不是旁路工具的读数。**

---

## 四、环境速查

| 项 | 值 |
| --- | --- |
| 操作系统 | Windows 11 家庭版 `10.0.26200` |
| 显示器 | 双屏，各 2560x1440，工作区 2560x1368（有任务栏） |
| 代理软件 | Clash，混合端口 `127.0.0.1:7890`，控制端口未开放（9090/9097/9091 均关闭） |
| DNS（github.com） | `20.205.243.166` |
| .NET SDK | `10.0.300` |
| 运行时 | `Microsoft.NETCore.App 10.0.8`、`Microsoft.WindowsDesktop.App 10.0.8` |
| PowerShell | 5.1（`powershell.exe`）；`pwsh` 不在 PATH |
| GitHub CLI | `gh 2.102.0`，账号 `yansizen-dev` |
| 登录方式 | 经典 PAT，scopes = `repo, workflow, read:org` |
