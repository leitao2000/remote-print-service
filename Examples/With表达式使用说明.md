# C# `with` 表达式使用说明

## 什么是 `with` 表达式？

`with` 表达式是 C# 9.0 引入的特性，用于创建现有对象的副本，并修改部分属性。它提供了一种简洁的方式来创建不可变对象的变体。

## 语法

```csharp
var newObject = originalObject with { Property1 = newValue1, Property2 = newValue2 };
```

## 支持的类型

### ✅ 支持 `with` 表达式的类型：

1. **Record 类型**（C# 9.0+）
   ```csharp
   public record Person
   {
       public string Name { get; init; }
       public int Age { get; init; }
   }
   
   var person1 = new Person { Name = "张三", Age = 25 };
   var person2 = person1 with { Age = 26 }; // ✅ 支持
   ```

2. **Record Struct**（C# 10.0+）
   ```csharp
   public record struct Point
   {
       public int X { get; init; }
       public int Y { get; init; }
   }
   
   var point1 = new Point { X = 10, Y = 20 };
   var point2 = point1 with { X = 15 }; // ✅ 支持
   ```

3. **Struct**（C# 10.0+）
   ```csharp
   public struct Point
   {
       public int X { get; set; }
       public int Y { get; set; }
   }
   
   var point1 = new Point { X = 10, Y = 20 };
   var point2 = point1 with { X = 15 }; // ✅ 支持（C# 10.0+）
   ```

### ❌ 不支持 `with` 表达式的类型：

1. **Class 类型**
   ```csharp
   public class Person
   {
       public string Name { get; set; }
       public int Age { get; set; }
   }
   
   var person1 = new Person { Name = "张三", Age = 25 };
   var person2 = person1 with { Age = 26 }; // ❌ 编译错误：CS8858
   ```

## 使用示例

### 示例 1: 基本用法

```csharp
public record PrintResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int RetryCount { get; init; }
}

// 创建初始响应
var response = new PrintResponse
{
    Success = true,
    Message = "打印成功",
    RetryCount = 0
};

// 使用 with 表达式添加重试次数
var responseWithRetry = response with { RetryCount = 3 };

// 使用 with 表达式修改为错误响应
var errorResponse = response with
{
    Success = false,
    Message = "打印失败",
    RetryCount = 2
};
```

### 示例 2: 在异常处理中使用

```csharp
public static PrintResponse HandleException(Exception ex, int retryCount)
{
    var baseResponse = new PrintResponse
    {
        Success = false,
        Message = ex.Message,
        ErrorCode = "UNKNOWN_ERROR",
        Timestamp = DateTime.UtcNow
    };

    // 使用 with 表达式添加重试次数和详细信息
    return baseResponse with
    {
        RetryCount = retryCount,
        ErrorDetails = ex.ToString()
    };
}
```

### 示例 3: 链式使用（C# 10.0+）

```csharp
var response = new PrintResponse { Success = true, Message = "成功" };

// 可以链式使用多个 with
var finalResponse = response
    with { RetryCount = 1 }
    with { ErrorCode = "TIMEOUT" };
```

## 重要特性

### 1. 不可变性

`with` 表达式**不会修改原对象**，而是创建一个新对象：

```csharp
var person1 = new Person { Name = "张三", Age = 25 };
var person2 = person1 with { Age = 26 };

Console.WriteLine(person1.Age); // 25（原对象未改变）
Console.WriteLine(person2.Age); // 26（新对象）
```

### 2. 浅拷贝

`with` 表达式执行浅拷贝，只复制值类型和引用：

```csharp
public record Person
{
    public string Name { get; init; }
    public List<string> Tags { get; init; } = new();
}

var person1 = new Person { Name = "张三", Tags = new List<string> { "A", "B" } };
var person2 = person1 with { Name = "李四" };

person2.Tags.Add("C");

Console.WriteLine(person1.Tags.Count); // 3（共享同一个列表引用）
Console.WriteLine(person2.Tags.Count); // 3
```

## 如何将 PrintResponse 改为 Record 类型

### 当前代码（Class）：

```csharp
public class PrintResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    // ...
}
```

### 改为 Record（支持 with 表达式）：

```csharp
public record PrintResponse
{
    public bool Success { get; init; }  // 改为 init
    public string Message { get; init; } = string.Empty;  // 改为 init
    public string? JobId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? ErrorCode { get; init; }
    public string? ErrorDetails { get; init; }
    public int RetryCount { get; init; }
}
```

### 使用 with 表达式：

```csharp
// 之前（Class，不支持 with）：
var errorResponse = PrintExceptionHandler.HandleException(ex, "打印操作");
errorResponse.RetryCount = retryCount;
return errorResponse;

// 之后（Record，支持 with）：
return PrintExceptionHandler.HandleException(ex, "打印操作")
    with { RetryCount = retryCount };
```

## 注意事项

1. **Record 是不可变的**：一旦创建，属性值不能修改（除非使用 `init` 访问器）
2. **需要 C# 9.0+**：`with` 表达式需要 C# 9.0 或更高版本
3. **Struct 支持需要 C# 10.0+**：普通 struct 的 `with` 支持需要 C# 10.0
4. **性能考虑**：`with` 表达式会创建新对象，对于频繁操作需要考虑性能

## 总结

- ✅ `with` 表达式适用于 **record**、**record struct** 和 **struct**（C# 10.0+）
- ❌ `with` 表达式**不适用于** **class** 类型
- 💡 如果需要在 class 中使用类似功能，需要手动创建新对象并复制属性
- 🎯 Record 类型更适合不可变数据模型，推荐用于 DTO、响应模型等

