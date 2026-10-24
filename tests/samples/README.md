# 测试样本目录

本目录**不附带任何真实客户端资源**。仓库里只有一份自己生成的合成样本，
需要真实 NPK 的用例请按下面的方式自备。

## 仓库自带的：`synthetic_shapes.NPK`

由 [`tools/make-sample`](../../tools/make-sample) 程序化生成，21 个 IMG、常规帧 80×80、
4~8 帧、全部 v2，内容是渐变方块、跳动圆、棋盘格、色条、圆环、斜条纹、旋转三角、
正弦渐变、点阵、圆角面板、横条、透明度渐变、旋转螺线、扫描扇形、正弦波，
外加一个含 LINK 帧的条目和一个帧尺寸递减的条目。

后 4 个（`effect_flame_burst` / `effect_light_beams` / `effect_plasma_arc` / `effect_particle_burst`，
均 192×192）是**特效型**贴图：不透明纯黑底（RGB 0,0,0 / alpha 255）上
叠加多层加色发光（火焰 / 三色光束 / 电弧 / 粒子爆散），专门用来演示「线性减淡」——切到该效果后
黑底被打穿成透明、只剩发光，和「原图」模式的黑方块形成明显对比。

每一帧都是数学公式画出来的色块，**不含任何游戏美术**，因此以 MIT 随仓库分发。

改过生成器后重新生成：

```bash
dotnet run --project tools/make-sample
```

输出是确定性的（伪随机只用固定种子的 LCG、无时间戳），重跑应当得到字节完全相同的文件。

## 自备真实样本

想跑"真实 NPK 逐字节往返"那批金标准用例，请在**本地**准备一份自己的样本
（例如从你自己安装的客户端里导出的包），命名为：

```
tests/samples/real_sample.NPK
```

该文件名已被 `.gitignore` 忽略，`git status` 里不会出现，也不会被误提交。
**请勿把任何第三方资源提交到本仓库。**

## 没有真实样本时会发生什么

`dotnet test` 仍然全绿：缺少 `real_sample.NPK` 时，依赖它的用例会在方法开头
`if (!SampleFiles.RealSampleAvailable) return;` 静默返回——不计入失败，也不打印任何内容
（不用 `Assert.Inconclusive`：那会让报告里的"跳过"数随本机有没有样本而变）。

CI 上因此**只覆盖合成样本**：容器格式、帧编辑、写回、UI 打开与保存这些主链路
全部由 `synthetic_shapes.NPK` 承担；只有"某份特定真实包的字节快照"这类断言会被跳过。

哪些用例会被跳过，见各测试类开头的注释；集中说明在
[CONTRIBUTING.md](../../CONTRIBUTING.md#测试样本)。
