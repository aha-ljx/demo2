# Demo2 工厂产线模拟

Unity 工厂产线搭建与生产模拟游戏。

## 在线游玩

[打开网页版游戏](https://aha-ljx.github.io/demo2/)

首次打开需要下载游戏资源。游戏使用键盘和鼠标操作，可点击播放器右下角切换全屏。

| 操作 | 按键 |
| --- | --- |
| 移动角色 / 鸟瞰视角 | W、A、S、D |
| 切换快慢速度 | Shift |
| 切换第三人称 / 鸟瞰 | CapsLock |
| 选择冲床、折弯机、焊接机、机械臂、滑槽 | 1～5 |
| 旋转设备 | R |
| 确认放置 | 鼠标左键 |
| 取消选择 / 鸟瞰撤回设备 | 鼠标右键 |
| 撤回附近设备 | B |
| 释放鼠标 | Esc |
| 打开操作教程 | H |

冲床的完整占地须位于黄线远离最终产物区的一侧。设备放置与人物重叠时，人物会被抬到设备上方。10 件成品全部入库后立即结算，无需等待 10 分钟倒计时结束；未全部完成时仍在 10 分钟到时结算。

## 本地开发

- Unity：2022.3.62f3c1。
- 模型与字体使用 Git LFS，克隆后请执行 `git lfs pull`。
- 启动场景：`Assets/Scenes/MainMenu.unity`。

## 导出与发布网页版

在 Unity Hub 中安装对应版本的 WebGL Build Support。Unity 菜单选择 **Tools → Factory → Build WebGL for GitHub Pages**，默认导出到 `Builds/WebGL`。

导出脚本启用 Gzip 压缩和解压回退，以适配 GitHub Pages。将导出目录的内容（包含 `.nojekyll`）提交到 `codex/github-pages` 分支根目录，GitHub Pages 从该分支发布。`main` 保留 Unity 源项目。
