namespace RemotePrintService.Examples
{
    /// <summary>
    /// with 表达式使用示例
    /// </summary>
    public class WithExpressionExample
    {
        /// <summary>
        /// 示例 1: 使用 record 类型（推荐）
        /// record 类型是 C# 9.0 引入的，专门用于不可变数据
        /// </summary>
        public record PersonRecord
        {
            public string Name { get; init; }
            public int Age { get; init; }
            public string? Email { get; init; }
        }

        /// <summary>
        /// 示例 2: 使用 record struct（C# 10.0+）
        /// </summary>
        public record struct PointRecord
        {
            public int X { get; init; }
            public int Y { get; init; }
        }

        /// <summary>
        /// 示例 3: 普通 struct（C# 10.0+ 支持）
        /// </summary>
        public struct PointStruct
        {
            public int X { get; set; }
            public int Y { get; set; }
        }

        /// <summary>
        /// 示例 4: 普通 class（不支持 with 表达式）
        /// </summary>
        public class PersonClass
        {
            public string Name { get; set; } = string.Empty;
            public int Age { get; set; }
        }

        /// <summary>
        /// 演示 with 表达式的用法
        /// </summary>
        public static void DemonstrateWithExpression()
        {
            // ========== 1. Record 类型使用 with 表达式 ==========
            var person1 = new PersonRecord
            {
                Name = "张三",
                Age = 25,
                Email = "zhangsan@example.com"
            };

            // 使用 with 表达式创建新对象，只修改部分属性
            var person2 = person1 with { Age = 26 }; // 只修改年龄
            var person3 = person1 with { Name = "李四", Email = "lisi@example.com" }; // 修改多个属性

            Console.WriteLine($"person1: {person1.Name}, {person1.Age}");
            Console.WriteLine($"person2: {person2.Name}, {person2.Age}"); // 年龄变为26，其他属性不变
            Console.WriteLine($"person3: {person3.Name}, {person3.Age}"); // 姓名和邮箱改变，年龄不变

            // ========== 2. Record Struct 使用 with 表达式 ==========
            var point1 = new PointRecord { X = 10, Y = 20 };
            var point2 = point1 with { X = 15 }; // 只修改 X 坐标
            Console.WriteLine($"point1: ({point1.X}, {point1.Y})");
            Console.WriteLine($"point2: ({point2.X}, {point2.Y})"); // (15, 20)

            // ========== 3. Struct 使用 with 表达式（C# 10.0+）==========
            var structPoint1 = new PointStruct { X = 5, Y = 10 };
            var structPoint2 = structPoint1 with { Y = 15 }; // 只修改 Y 坐标
            Console.WriteLine($"structPoint1: ({structPoint1.X}, {structPoint1.Y})");
            Console.WriteLine($"structPoint2: ({structPoint2.X}, {structPoint2.Y})"); // (5, 15)

            // ========== 4. Class 类型不支持 with 表达式 ==========
            var classPerson1 = new PersonClass { Name = "王五", Age = 30 };
            
            // 以下代码会编译错误：
            // var classPerson2 = classPerson1 with { Age = 31 }; // ❌ 错误：CS8858

            // 对于 class，需要手动创建新对象：
            var classPerson2 = new PersonClass
            {
                Name = classPerson1.Name, // 复制所有属性
                Age = 31 // 修改需要的属性
            };
        }

        /// <summary>
        /// 实际应用示例：在打印服务中使用
        /// </summary>
        public static void PrintServiceExample()
        {
            // 如果 PrintResponse 是 record 类型，可以这样使用：
            /*
            var response1 = new PrintResponse
            {
                Success = true,
                Message = "打印成功",
                JobId = "job-123",
                Timestamp = DateTime.UtcNow
            };

            // 需要添加重试次数时，使用 with 表达式
            var response2 = response1 with { RetryCount = 3 };

            // 需要修改错误信息时
            var errorResponse = response1 with 
            { 
                Success = false, 
                Message = "打印失败",
                ErrorCode = "PRINTER_OFFLINE"
            };
            */
        }
    }
}



