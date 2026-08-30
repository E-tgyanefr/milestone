# 部署配方执行记录（eng-coder 根因定位——exit=2 消除）

- **根因**（eng-coder 定位）：exit=2=Program.cs catch DllNotFoundException（Interop.cs const Dll=rhygemaker 普通 DllImport——加载靠 exe 同目录+PATH）；缺 4 dll（rhygemaker.dll+libgcc_s_seh-1.dll+libstdc++-6.dll+libwinpthread-1.dll——MinGW 运行时）。
- **配方**（游戏层部署配置——零引擎改动）：build\src\bind\rhygemaker.dll（SHA256=991CA5C1ED458D0A3B830B519E147FF718C1962AF486133EF52AD5A33AB4852B canonical 锁组）+3 MinGW 运行时复制到 exe 同目录。
- **执行**：镜像树（引擎源码\rhygemaker\dotnet\Milestone.Game\bin\Release\net8.0）原缺 4 dll——已从发布树（canonical）复制；ASCII 树已齐（pre-部署）。
- **验证**（镜像树 exe，native 在位）：--frames 60 RC=0 roots=9 liveEngines=1；--frames 1400 冒烟完成（菜单→曲库→选歌→游玩→结果无异常）；--abi-check **PASS 22/22**（重建后）；两树 4 dll 齐备+源码 31 文件 MD5=0。
- **基准**：3 树部署面（ASCII/mirror/发布）现全部可运行（4 dll 齐+canonical rhygemaker.dll）；exit=2 根因彻底消除。