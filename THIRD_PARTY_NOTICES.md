# Third-Party Notices — dogdesk v0.8.0

dogdesk 本体为 MIT 许可的独立项目。以下开源项目/产品被作为**功能参考或独立进程**使用；除明确标注外，dogdesk **未复制其代码/素材**（功能级自研实现），保留各自版权与许可如下。

## 直接参考（效果/思路，功能级自研，未搬代码）

| 项目 | 许可 | 用途 | 声明 |
|---|---|---|---|
| [AF Media Bar](https://github.com/Fervent-Tempo/AF-Media-Bar)（Fervent-Tempo） | MIT | 任务栏媒体控制条（v0.4 起媒体条形态；v0.8 嵌入/悬浮双模式、滚轮音量、两行歌词、材质） | MIT © Fervent-Tempo；dogdesk 按效果功能级自研，未搬其代码 |
| [BongoCat（原版）](https://github.com/ayangweb/BongoCat)（ayangweb） | **Apache-2.0** | 桌宠敲键盘效果（键盘画面/键帽高亮/爪击/多表情） | Apache-2.0 © ayangweb；移植/参考其实现思路处已在源码文件头保留版权声明；Apache License 2.0 全文见 https://www.apache.org/licenses/LICENSE-2.0 |
| [Win11Debloat](https://github.com/Raphire/Win11Debloat)（Raphire） | MIT | 系统优化模块部分命令参考 | MIT；仅参考命令清单，未复制脚本 |

## 仅效果对标（未搬任何代码）

| 项目 | 许可 | 声明 |
|---|---|---|
| [BongoCat（重构版）](https://github.com/vladelaina/BongoCat)（vladelaina） | **AGPL-3.0** | **未复制其任何代码/素材**（AGPL 传染性，dogdesk 为 MIT）；仅对标"重构版"视觉与流畅度效果，全部功能级自研 |
| Wallpaper Engine（Wallpaper Engine team） | 闭源商业（Steam） | 未复制任何代码/素材；仅对标常见效果（视频/网页/粒子/音频响应/鼠标互动/多显示器/性能降载），全部自研 |

## NuGet 依赖（包引用）

| 包 | 许可 | 用途 |
|---|---|---|
| Microsoft.Web.WebView2 | MIT | HTML 网页壁纸（v0.8.0，默认关闭）；仅本机只读浏览本地 html，隐藏 DevTools/右键菜单 |

## 独立进程分发（不链接入二进制、不修改源码）

| 项目 | 许可 | 声明 |
|---|---|---|
| [aria2](https://github.com/aria2/aria2) | **GPL-2.0** | 下载加速内核，以独立进程方式调用（`aria2c`，-x16 -s16 多线程分段、--continue 断点续传）；**不修改其源码、不链接入本项目二进制**；Windows 可放置 `aria2/aria2c.exe` 于程序目录或加入 PATH，macOS 可 `brew install aria2`。仅用于下载用户拥有权限的合法链接，不实现任何网盘绕过/破解/盗链 |

## 既有功能级自研参考（历史轮次保留声明）

| 项目 | 许可 | 声明 |
|---|---|---|
| Folia | AGPL | 全屏歌词动画等，功能级自研，未搬代码 |
| DeskBox | GPL-3.0 | 桌面整理，功能级自研，未搬代码 |
| Winhance | GPL-3.0 | 系统优化部分思路，功能级自研 |
| Mineradio | GPL | 3D 环绕音效等，功能级自研，未搬代码 |
| ZenDesktop | GPL-3.0 | 液态玻璃：仅封装 Windhawk mod 安装/卸载脚本（默认不勾选，红风险），未复制 mod 代码 |
| Neuro-sama / OpenSpeedy / Lanboo 兰博 / AmorFate | 视频/演示参考 | 仅效果对标，功能级自研 |
