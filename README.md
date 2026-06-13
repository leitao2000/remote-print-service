# RemotePrintService 操作手册

## 1. 项目简介

RemotePrintService 是基于 ASP.NET Core 的远程打印服务，提供 RESTful API 用于：

- 获取本地/网络打印机列表与状态
- 执行打印任务（支持 PDF、TXT 等）
- 生成/打印标签 PDF

服务默认监听端口：`5035`

---

## 2. 环境与依赖

- Windows 10/11 或 Windows Server 2016+
- .NET SDK（项目目标：.NET 10）
- 打印机驱动已安装并可用
- PDF 打印建议方案：
  - 推荐：SumatraPDF（可命令行静默打印，适合 Windows 服务）
  - 可选：Adobe Acrobat/Reader（需要桌面交互，服务模式可能受限）

---

## 3. 快速开始（本地运行）

### 3.1 还原与运行

```powershell
cd remote-printing-service
dotnet restore
dotnet run
```

启动后访问 API 文档：

- Scalar API：`http://localhost:5035/scalar/v1`
- OpenAPI JSON：`http://localhost:5035/openapi/v1.json`

### 3.2 验证服务

```powershell
Invoke-RestMethod http://localhost:5035/api/printer/list
```

---

## 4. 配置说明

配置文件：`appsettings.json`

### 4.1 监听与基础配置

服务在 `Program.cs` 中绑定 `0.0.0.0:5035`。如需修改端口，请修改 `Program.cs` 中的 `ListenAnyIP(5035)`。

### 4.2 打印配置示例

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "PdfPrint": {
    "AdobeAcrobatPath": "C:\\Program Files\\Adobe\\Acrobat DC\\Acrobat\\Acrobat.exe",
    "UseShellExecute": true,
    "PaperSize": {
      "Width": 80,
      "Height": 60,
      "Unit": "mm"
    }
  },
  "PrintSettings": {
    "ImmediatePrint": true
  }
}
```

> 提示：如果后续接入 SumatraPDF，建议新增配置项 `PdfPrint:SumatraPdfPath` 并在代码中优先使用该工具。

---

## 5. Windows 服务化部署

### 5.1 发布

```powershell
dotnet publish -c Release -o ./publish
```

### 5.2 注册为 Windows 服务

```powershell
sc create RemotePrintService binPath= "C:\Path\To\publish\RemotePrintService.exe" start= auto
sc start RemotePrintService
```

### 5.3 服务模式注意事项

- Windows 服务运行在 Session 0，GUI 程序可能无法正常交互。
- 打印 PDF 推荐使用支持命令行静默打印的工具（如 SumatraPDF），避免依赖 GUI 程序。

---

## 6. API 使用指南

### 6.1 打印机管理

- 获取打印机列表

```powershell
Invoke-RestMethod http://localhost:5035/api/printer/list
```

- 获取指定打印机信息

```powershell
Invoke-RestMethod http://localhost:5035/api/printer/{printerName}
```

- 检查打印机状态

```powershell
Invoke-RestMethod http://localhost:5035/api/printer/{printerName}/status
```

- 诊断环境

```powershell
Invoke-RestMethod http://localhost:5035/api/printer/diagnostics
```

### 6.2 执行打印

```powershell
$body = @{
  filePath = "C:\Temp\doc.pdf"
  printerName = "HP LaserJet"
  copies = 1
  doubleSided = $false
  paperSize = "A4"
  orientation = "Portrait"
} | ConvertTo-Json

Invoke-RestMethod -Method Post -Uri http://localhost:5035/api/printer/print -Body $body -ContentType "application/json"
```

### 6.3 标签打印

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5035/api/label/generate-label -Body '{"number": 123}' -ContentType "application/json"
```

---

## 7. 常见问题与排查

### 7.1 首次请求慢或失败

- 服务启动时会进行打印机系统预热，首次请求可能有延迟。
- 检查 `diagnostics` 接口确认打印机可见性。

### 7.2 PDF 打印失败

- 确认 PDF 路径可访问，文件未被锁定。
- 检查打印机名称与状态是否正确。
- 若运行在 Windows 服务，避免使用需要桌面的 Acrobat，改用命令行打印工具。

### 7.3 权限问题

- 确保运行服务的账户有权访问打印机与文件目录。
- 对于网络打印机，确认网络连通性与端口开放。

---

## 8. 日志与监控

- 控制台输出集成了 Spectre.Console，便于阅读。
- 应用日志通过 ASP.NET Core Logging 输出到控制台/文件（按配置）。
- 关键操作建议结合 Windows 事件查看器与应用日志联合排查。

---

## 9. 安全建议

- 生产环境请启用 HTTPS 并配置授权策略。
- 对外开放 API 建议加认证鉴权，避免未授权打印。
- 限制可打印目录与文件类型，降低风险。

---

## 10. 维护与升级

- 升级时先备份 `appsettings.json` 与发布目录。
- 升级 .NET 版本前确认目标框架与依赖兼容性。
- 如需回滚，保留上一版本发布包即可快速切换。
