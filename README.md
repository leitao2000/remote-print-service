# RemotePrintService 远程打印服务

## 项目概述

RemotePrintService 是一个基于 ASP.NET Core 的远程打印服务，支持本地打印机和网络打印机的打印操作。该服务提供了 RESTful API 接口，可以通过 HTTP 请求执行各种打印任务。

本项目集成了 [Spectre.Console](https://github.com/spectreconsole/spectre.console) 来提供美观的控制台输出效果，包括彩色文本、表格、进度条等。

## 功能特性

- 获取可用打印机列表
- 检查打印机状态（在线/离线）
- 查询打印队列任务数
- 支持多种文件格式打印（PDF、TXT等）
- 支持文件上传并直接打印
- 支持指定打印份数、双面打印等参数
- 使用 Spectre.Console 提供美观的控制台输出

## 技术栈

- ASP.NET Core 9.0
- Scalar.AspNetCore（API 文档）
- System.Drawing.Common（打印支持）
- PDFsharp（PDF 处理）
- Spectre.Console（增强控制台输出）

## 项目结构

```
RemotePrintService/
├── Controllers/           # 控制器层
│   ├── PrinterController.cs    # 打印机相关API控制器
│   └── LabelController.cs      # 标签生成和打印API控制器
├── Models/                # 数据模型
│   ├── PrintRequest.cs         # 打印请求模型
│   ├── PrintResponse.cs        # 打印响应模型
│   └── PrinterInfo.cs          # 打印机信息模型
├── Services/              # 服务层
│   ├── IPrinterService.cs      # 打印机服务接口
│   ├── LocalPrinterService.cs  # 本地打印机服务实现
│   └── NetworkPrinterService.cs# 网络打印机服务实现
├── Properties/            # 项目属性配置
├── appsettings.json       # 应用程序配置文件
└── Program.cs             # 应用程序入口点
```

## 配置说明

### appsettings.json

主要的应用程序配置文件，包含以下配置项：

- Logging: 日志配置
- AllowedHosts: 允许的主机地址
- PdfPrint: PDF 打印相关配置
  - AdobeAcrobatPath: Adobe Acrobat 应用程序路径
  - PaperSize: 纸张尺寸配置

### 网络打印机配置示例

如果需要使用网络打印机，可以在 appsettings.json 中添加如下配置：

```json
"Printers": {
  "NetworkPrinters": [
    {
      "Name": "NetworkPrinter1",
      "IpAddress": "192.168.1.100",
      "Port": "9100",
      "IsDefault": true,
      "Description": "办公室网络打印机"
    }
  ],
  "DefaultPrinter": "NetworkPrinter1"
}
```

### launchSettings.json

开发环境的启动配置，定义了不同的运行环境：

- http: HTTP 协议运行配置
- https: HTTPS 协议运行配置
- IIS Express: IIS Express 运行配置

## API 接口说明

### 打印机管理接口

#### 获取打印机列表
```
GET /api/printer/list
```

#### 获取打印机信息
```
GET /api/printer/{printerName}
```

#### 检查打印机状态
```
GET /api/printer/{printerName}/status
```

#### 获取打印队列任务数
```
GET /api/printer/{printerName}/queue-count
```

#### 执行打印操作
```
POST /api/printer/print
Content-Type: application/json

{
  "filePath": "文件路径",
  "printerName": "打印机名称（可选）",
  "copies": 1,
  "doubleSided": false,
  "paperSize": "A4",
  "orientation": "Portrait"
}
```

#### 上传文件并打印
```
POST /api/printer/print-file
Content-Type: multipart/form-data

文件参数: file
打印机名称: printerName（可选）
打印份数: copies（可选）
双面打印: doubleSided（可选）
纸张尺寸: paperSize（可选）
```

### 标签生成接口

#### 生成标签PDF
```
POST /api/label/generate-label
Content-Type: application/json

{
  "number": 123
}
```

#### 生成标签并打印
```
POST /api/label/print-label
Content-Type: application/json

{
  "number": 123,
  "printerName": "打印机名称（可选）"
}
```

## 使用说明

1. 确保系统已安装所需的打印机驱动程序
2. 对于 PDF 打印，需要安装 Adobe Acrobat 或 Adobe Reader
3. 配置 appsettings.json 中的打印机相关参数
4. 运行应用程序
5. 通过 API 接口执行打印操作

## 控制台输出增强

本项目使用 Spectre.Console 库来提供增强的控制台输出效果：

### 启动画面
- 使用 Figlet 字体显示应用标题
- 彩色文本显示服务状态信息

### 运行时输出
- 彩色日志信息（绿色表示成功，黄色表示警告，红色表示错误）
- 表格形式显示打印机列表
- 进度条显示长时间运行的操作

### 示例输出效果
```
____ ____ ___  ____ ____ ____ _  _ ____ ___ ____ ____ 
|__/ |___ |__] |  | |__/ |___ |\ | | __  |  |___ [__  
|  \ |___ |    |__| |  \ |___ | \| |__]  |  |___ ___]

正在启动远程打印服务...

远程打印服务启动完成！

运行环境: Development
监听地址: http://localhost:5035
API 文档地址: http://localhost:5035/scalar/v2-beta
```

## 日志记录

应用程序使用内置的日志系统记录操作日志和错误信息，便于问题排查和系统监控。

## 错误处理

服务具有完善的错误处理机制，对于各种异常情况都会返回相应的错误信息和 HTTP 状态码。
