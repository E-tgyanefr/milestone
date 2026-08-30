# t126：v3 3D 模式设计（软件光栅化 + 编辑器 3D 视图）

> eng-design-vis · 用户指令：增加 3D 模式。基线：RhygeMaker——M0-M2.6（Core/Platform/Assets/绑定 C++·C#·Python/Editor）+ M1 软渲（9 原语屏幕空间）+ M2 资产；3D=新渲染面（与 2D 共存，零第三方红线内）。
> 产出：透视相机/投影/深度缓冲/Mesh/三角形光栅化+光照 + 编辑器 3D 视图 + demo（cube/pyramid）renderhash + 验收 + 决策点。只做设计。
> 命名：模块=Milestone::Render3D（include/milestone/render3d/*，milestone_render3d STATIC——依赖 core+platform；零第三方）。

---

## 0. 方案要点（先读）

1. **渲染管线分层**：3D 世界面（Render3D：MVP→透视除法→near 裁剪→背面剔除→三角形光栅化+z-buffer+兰伯特光照→渲染到自身 color+z 目标）→ **合成**（BlitRect 到 IRenderer 帧缓冲——UI/2D 原语其后不变）——Unity 序（3D 世界→UI 上层）；2D parity 黄金帧保持（3D 独立路径）。
2. **坐标约定**：右手系、Y 上、-Z 前（Unity 一致）；投影矩阵=标准透视（FOV-Y，near/far），空间裁剪=camera 空间 near 平面裁剪（多边形近平面裁剪——扫描线后裁剪复杂，多边形裁剪稳）；环绕序+背面剔除（CCW=正面，默认剔除背面）。
3. **深度缓冲**：float32 z（一通道；P2=24bit 打包）；z 测试=less（近者胜）；写=保守（不透明）。
4. **光照**：单方向光 + 环境光（flat Lambert：color * (ambient + diffuse * max(dot(n,L),0))）；逐像素（重心插值法线）；Phong/镜面=P2。
5. **渲染目标**：Render3D target = 单独 color（Rgba）+ z 缓冲（可复用每帧）；合成到 IRenderer via **新增 IRenderer::BlitRect(x,y,w,h,const Rgba*)**（一次 blit——接口小增，SoftwareRenderer 实现；不影响既有 9 原语）。
6. **场景集成**：MeshRenderer 组件（GameObject 挂载：mesh 引用（.mstri）+颜色+启用；Transform→世界矩阵）；活跃相机=带 Camera3D 组件的对象（无则默认相机）；渲染遍历=每帧收集 MeshRenderer（M0 直接列表；场景遍历 P1/编辑器 M3D.2）。

---

## 1. 签名级（render3d/*）

~~~cpp
namespace Milestone::Render3D {
// —— 相机 ——
struct Camera3D {                                // 组件（GameObject 挂载）；M0 默认=引擎渲染设置回退
  Vec3 Position() const;  void SetPosition(const Vec3&);
  Quat Orientation() const; void SetOrientation(const Quat&);
  double FovDeg() const; void SetFovDeg(double);           // 垂直 FOV（35..120；默认 60）
  double Near() const; void SetNear(double);  double Far() const; void SetFar(double);   // 默认 0.1 / 1000
  bool Orthographic() const; void SetOrthographic(bool);   // 正交切换（编辑器 2D/3D 模式）；ortho=box(orthoSize,aspect)
};
// —— 矩阵（数学复用+新）——
Mat4 MakePerspective(double fovY, double aspect, double nearZ, double farZ);   // 右手（-Z 前）
Mat4 MakeOrthographic(double halfW, double halfH, double nearZ, double farZ);
Mat4 MakeView(const Vec3& eye, const Quat& orient);                            // 逆世界矩阵（LookAt 便捷=LookAt(eye,target,up)）
Mat4 MakeLookAt(const Vec3& eye, const Vec3& target, const Vec3& up);
// —— Mesh ——
struct MeshVertex { Vec3 pos; Vec3 normal; };            // uv=P2
struct MeshData {
  std::string name; std::vector<MeshVertex> vertices;    // 三角形顶点（非索引展开——M0 简单（9-12 tri 小 mesh）；索引=P2）
  Rgba baseColor;                                        // 材质基础色（flat——贴图=P2）
  bool hasNormals() const; void ComputeNormals();         // 缺省=面法线（三角形叉积）
};
MeshData MakeCube(double size);                          // 8 顶点/12 三角（面基色=分别可设——M0 单色）编辑
MeshData MakePyramid(double base, double height);        // 5 顶点/6 三角
// —— 着色与提交 ——
struct RasterOptions { Rgba clear = {8,10,18,255}; Rgba ambient = {30,30,36,255};
  Vec3 lightDir = {-0.5,-0.7,-0.5};                       // 归一化方向光
  Vec3 lightColor = {1,1,1}; double lambert = 0.85;  bool cullBackface = true; };
class Render3D {
public:
  bool Begin(int w, int h);                              // 分配/复用 color+z 目标（尺寸=视口）
  void SetCamera(const Camera3D&, double aspect, double deviceScale=1.0);   // MVP 预计算
  void Submit(const MeshData&, const Mat4& world, Rgba tint = {});          // 世界矩阵+基色
  void End();                                            // 完成（光栅化已发生——见 Draw 语义）
  void DrawMesh(const MeshData&, const Mat4& world, Rgba tint);            // =Submit+立即光栅化（M0 便捷）
  const Rgba* Color() const;  const float* Depth() const;                  // 目标访问（合成/测试）
  void Composite(IRenderer& r, int dx, int dy);          // BlitRect 到帧缓冲
  // 测试辅助
  void ProjectPoint(const Vec3& world, double& sx, double& sy, double& sz); // MVP→屏幕（断言用）
private: /* color_/depth_/mvp_/视口状态 */
};
// —— 组件（场景集成）——
class MeshRenderer : public Milestone::Core::Component {
public:
  MeshData mesh;  Rgba color{255,255,255,255};  bool enabled{true};   // 字段（RHYGEMAKER_REFLECT）
  // 每帧 Render3D.Submit(mesh, Owner()->GetTransform()->WorldMatrix(), color)
};
class Camera3DComponent : public Milestone::Core::Component {
public:
  Camera3D cam;  int priority = 0;  bool active = true;                 // 活跃相机筛选（priority 高者）
};
}
~~~

### 1.1 三角形光栅化（内部算法——确定性与性能）

- 顶点变换：pos' = MVP * pos（camera 空间先 near 裁剪：frustum near 平面 z<=-near 的多边形裁剪（Sutherland-Hodgman 单平面——最多 4 顶点→6）→ 透视除法（w>0 保证）→ NDC→屏幕（xscale = W/2, yscale = -H/2，y 轴翻转））。
- 剔除：camera 空间 backface=cross(edge0,edge1) 点乘 viewDir > 0（右手 CCW 正面——剔除背面）。
- 光栅化：屏幕空间边界框+边函数（barycentric 半空间——非负=在三角形内）；每像素 z 由重心插值（NDC z——线性格网），**z-buffer less 测试**通过→写 color+normal 插值。
- 插值：重心坐标（float）；法线插值→归一化→光照（flat Lambert——像素级）；颜色=baseColor * light + ambient。
- 确定性：全部 float32 固定流程（无随机）——**3D 黄金帧 hash 稳定**（--renderhash3d）。
- 性能纪律：目标>60fps 简单场景（<1k tri）；无堆分配热路径（复用缓冲）；P2 优化=分块/BVH（后置）。

## 2. 资产（.mstri——JSON 文本资产）

~~~json
{ "mstri": "1", "name": "Cube", "material": { "color": "#8A5CF6" },
  "triangles": [
    { "a": [0,0,0], "b": [1,0,0], "c": [0,1,0] }, ... ] }
~~~

- AssetDatabase 类型表扩展：type=mstri（.mstri）——Load<MeshAsset>（解析→MeshData；正常数缺省计算）；侧车 .msmeta 同 M2。
- 内建：MakeCube/MakePyramid（demo+编辑器默认体）；cookbook 示例=M0 demo。

## 3. 编辑器 SceneView 3D 视图（M3D.2）

| 能力 | 行为 | 批 |
|---|---|---|
| 视口切换 | 2D↔3D 模式（SceneView 工具按钮；2D=现有正交渲染；3D=Render3D 相机呈现） | M3D.2 |
| 相机控制 | 中键拖拽=环绕（orbit：yaw/pitch）/右键拖拽=平移/滚轮=dolly（Unity 习惯） | M3D.2 |
| 网格轴 | 地面网格（XZ 平面+25% 步线）+X 红/Z 蓝 轴向线 | M3D.2 |
| 选中 | 线框高亮（M0 点集连框；P1 完整拖拽手柄） | P1 |
| 相机组件 | 场景相机对象（Camera3DComponent）→SceneView 跟随（优先） | M3D.2 |

## 4. Demo 与 renderhash

- **demo3d（ms_demo --renderhash3d / py_demo / csdemo 对称扩展）**：固定场景=旋转立方体（8 顶点 12 三角面基色）+角锥（6 三角）+方向光；相机固定（eye=(3,2,4) lookAt origin fov60）；每帧旋转角=固定步（确定性——帧序固定）；120 帧后读 Render3D target FNV（C 端直接出 hash——不重复实现）。
- **3D 黄金帧=独立常量**（与 2D parity 4634E387E024BE90 分离；首跑生成——三语言共享同一 Render3D 路径（C++ 核）——C#/Python 经绑定调用 demo3d 模式取 hash；无逐像素跨语言差异（同核同路径））。
- **2D parity 保持**：--renderhash 不动（3D 独立路径不触碰 2D 管线）。

## 5. 验收清单（M3D 全绿）

| # | 项 | 判据 |
|---|---|---|
| 1 | 矩阵 | MakePerspective 断言（已知眼/cube 投影到预期屏幕坐标±0.5px）；MakeView/MakeLookAt 逆一致性；正交/透视切换 |
| 2 | 光栅化 | 三角形填色断言（已知屏幕空间三角形→指定像素颜色） |
| 3 | 深度遮挡 | 前后两四边形（近者胜）→重叠区=近者颜色（z-buffer 断言） |
| 4 | 背面剔除 | 背面朝向三角形→不渲染（像素计数=0 区域断言） |
| 5 | 光照 | Lambert：正面≈baseColor*(ambient+diffuse)（像素近似断言）；背面/法线朝光源=更亮 |
| 6 | Mesh | MakeCube/MakePyramid 顶点/三角计数+法线（面法线 dot 断言）+.mstri 资产 round-trip（AssetDatabase） |
| 7 | 相机移动 | 两帧（眼位移动）→目标帧差异（非零像素比例断言——图像确实变） |
| 8 | 合成顺序 | Render3D 在 2D/UI 之前（3D 世界→BlitRect→UI 覆盖——像素断言：UI 位于 3D 之上） |
| 9 | parity/稳定性 | --renderhash3d 两次运行一致+黄金常量；--renderhash（2D）保持不变 |
| 10 | 编辑器 3D | SceneView 3D 模式渲染非空+orbit/zoom 变化帧差异+2D↔3D 切换断言 |

## 6. 决策点（captain 拍板）

| # | 决策 | 建议 |
|---|---|---|
| M3D-1 | 光照优先 | M0=flat Lambert+ambient（单方向光）；Phong/镜面/多光/贴图=P2 |
| M3D-2 | 坐标系 | 右手/Y 上/-Z 前（Unity 一致） |
| M3D-3 | 深度格式 | float32 z（一通道）；24bit 打包=P2 |
| M3D-4 | 相机域 | Camera3D 组件（GameObject 挂载+priority/active）；无则默认相机——Unity 式（采纳） |
| M3D-5 | BlitRect | IRenderer 增 BlitRect（9→10 原语；SoftwareRenderer 实现；2D 不受影响） |
| M3D-6 | Mesh 索引 | M0=三角形展开（<1k tri demo）；索引/P2 |
| M3D-7 | 混合 | 不透明 M0（alpha 混合/半透明=P2） |
| M3D-8 | renderhash | 3D 独立黄金帧（--renderhash3d）；2D parity 不变 |

---

*t126 完成*（软件光栅化面（MVP/near 裁剪/剔除/重心光栅化+z-buffer+兰伯特）+Camera3D/Mesh/.mstri/MeshRenderer 组件+编辑器 3D 视图+demo cube/pyramid+验收 10 项+M3D-1..8；交付 eng-coder-vis 实现）