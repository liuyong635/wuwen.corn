# TODO List

## 🖐️ 手工任务

- 在 VisionMaster 中添加全局变量 %WhiteAreaRatio%（float，白色区域占比0-100）
- 在 VisionMaster 中添加脚本4（白色占比计算），位置在脚本2之后
- 配置 PLC 信号地址（挡料板控制，需要在 VibratorConfig 中配置）
- 测试排料流程（挡料板+振动2）
- 标定白色占比阈值参数（WhiteAreaThreshold，默认50%）
- 标定空振次数阈值参数（EmptyVibrateCount，默认5次）
- 标定排料持续时间参数（DrainDuration，默认5秒）

## 🤖 代码任务

- 修改 DiskVisionHandler 集成排料和抖料逻辑（新增 ExecuteDrainSequenceAsync 私有方法）
