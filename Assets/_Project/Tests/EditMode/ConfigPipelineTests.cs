using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Template.Config;
using Template.Config.EditorTools;
using Template.Config.Generated;
using UnityEngine;

namespace Template.Tests
{
    /// <summary>CSV 解析器：表定义约定与容错（引号、注释、列数不符）。</summary>
    public class ConfigCsvParserTests
    {
        [Test]
        public void Parse_ReadsFieldsTypesAndRows()
        {
            const string csv = "id,name,price\nint,string,int\n1001,木剑,30\n1002,铁剑,120\n";

            Assert.IsTrue(ConfigCsvParser.TryParse("Item", "Assets/Data/Item.csv", csv, out CsvTable table, out string error), error);

            Assert.AreEqual(new[] { "id", "name", "price" }, table.Fields);
            Assert.AreEqual(new[] { "int", "string", "int" }, table.Types);
            Assert.AreEqual(2, table.Rows.Count);
            Assert.AreEqual(new[] { "1002", "铁剑", "120" }, table.Rows[1]);
        }

        [Test]
        public void Parse_HandlesQuotedFieldsWithCommasAndEscapedQuotes()
        {
            const string csv = "id,note\nint,string\n1,\"含,逗号的\"\"说明\"\"\"\n";

            Assert.IsTrue(ConfigCsvParser.TryParse("Item", "Assets/Data/Item.csv", csv, out CsvTable table, out string error), error);

            Assert.AreEqual("含,逗号的\"说明\"", table.Rows[0][1], "引号包裹的字段里可以有逗号，\"\" 表示一个引号");
        }

        [Test]
        public void Parse_SkipsBlankAndCommentLines()
        {
            const string csv = "# 这是备注\nid,name\nint,string\n\n1001,木剑\n";

            Assert.IsTrue(ConfigCsvParser.TryParse("Item", "Assets/Data/Item.csv", csv, out CsvTable table, out string error), error);

            Assert.AreEqual(1, table.Rows.Count, "空行与 # 注释行应被跳过");
            Assert.AreEqual("1001", table.Rows[0][0]);
        }

        [Test]
        public void Parse_TooFewLines_Fails()
        {
            Assert.IsFalse(ConfigCsvParser.TryParse("Item", "p", "id,name\n", out _, out string error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void Parse_ColumnCountMismatch_Fails()
        {
            const string csv = "id,name\nint,string\n1001,木剑,多出来的一列\n";

            Assert.IsFalse(ConfigCsvParser.TryParse("Item", "p", csv, out _, out string error));
            StringAssert.Contains("列", error);
        }
    }

    /// <summary>CSV 编码检查：能抓的抓（非法字节 / BOM），抓不到的靠约定（一律存 UTF-8）。</summary>
    public class ConfigEncodingTests
    {
        [Test]
        public void IsValidUtf8_AcceptsUtf8Text()
        {
            Assert.IsTrue(CsvEncodingCheck.IsValidUtf8(Encoding.UTF8.GetBytes("id,name\nint,string\n1001,木剑\n")));
        }

        [Test]
        public void IsValidUtf8_RejectsInvalidBytes()
        {
            Assert.IsFalse(CsvEncodingCheck.IsValidUtf8(new byte[] { 0x31, 0xFF, 0xFE }),
                "0xFF/0xFE 不是合法 UTF-8 字节");
        }

        [Test]
        public void HasBom_DetectsExcelUtf8Bom()
        {
            Assert.IsTrue(CsvEncodingCheck.HasBom(new byte[] { 0xEF, 0xBB, 0xBF, 0x69, 0x64 }));
            Assert.IsFalse(CsvEncodingCheck.HasBom(Encoding.UTF8.GetBytes("id")));
        }
    }

    /// <summary>导入前校验：坏数据必须在导入阶段就被拦住。</summary>
    public class ConfigValidatorTests
    {
        private static CsvTable Table(string name, string header, string types, params string[] rows)
        {
            string csv = $"{header}\n{types}\n{string.Join("\n", rows)}\n";
            Assert.IsTrue(ConfigCsvParser.TryParse(name, "Assets/Data/" + name + ".csv", csv, out CsvTable table, out string error), error);
            return table;
        }

        [Test]
        public void Validate_GoodTables_Pass()
        {
            var item = Table("Item", "id,name", "int,string", "1001,木剑", "1002,铁剑");
            var enemy = Table("Enemy", "id,dropItem", "int,ItemConfig", "1,1001", "2,");

            Assert.IsEmpty(ConfigValidator.Validate(new[] { item, enemy }));
        }

        [Test]
        public void Validate_DuplicateId_Reports()
        {
            var table = Table("Item", "id,name", "int,string", "1001,木剑", "1001,铁剑");

            List<string> errors = ConfigValidator.Validate(new[] { table });

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("重复", errors[0]);
        }

        [Test]
        public void Validate_MissingIdColumn_Reports()
        {
            var table = Table("Item", "name,price", "string,int", "木剑,30");

            List<string> errors = ConfigValidator.Validate(new[] { table });

            Assert.IsNotEmpty(errors);
            StringAssert.Contains("id", errors[0]);
        }

        [Test]
        public void Validate_UnknownType_Reports()
        {
            var table = Table("Item", "id,damage", "int,Vector3", "1001,1");

            List<string> errors = ConfigValidator.Validate(new[] { table });

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("未知类型", errors[0]);
        }

        [Test]
        public void Validate_ValueNotParsable_Reports()
        {
            var table = Table("Item", "id,price", "int,int", "1001,不是数字");

            List<string> errors = ConfigValidator.Validate(new[] { table });

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("无法解析", errors[0]);
        }

        [Test]
        public void Validate_ForeignKeyToMissingRow_Reports()
        {
            var item = Table("Item", "id,name", "int,string", "1001,木剑");
            var enemy = Table("Enemy", "id,dropItem", "int,ItemConfig", "1,2002");

            List<string> errors = ConfigValidator.Validate(new[] { item, enemy });

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("不存在", errors[0]);
        }

        [Test]
        public void Validate_ForeignKeyToMissingTable_Reports()
        {
            var enemy = Table("Enemy", "id,dropItem", "int,ItemConfig", "1,1001");

            List<string> errors = ConfigValidator.Validate(new[] { enemy });

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("指向的表 Item 不存在", errors[0]);
        }
    }

    /// <summary>配置表的运行期查找语义（用测试自带的表类型，不依赖已导入的资产）。</summary>
    public class ConfigTableTests
    {
        private sealed class TestRow : IConfigRow
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        private sealed class TestTable : ConfigTableBase<TestRow>
        {
        }

        private TestTable _table;

        [SetUp]
        public void SetUp()
        {
            _table = ScriptableObject.CreateInstance<TestTable>();
            _table.EditorReplaceRows(
                new List<object>
                {
                    new TestRow { Id = 1001, Name = "木剑" },
                    new TestRow { Id = 1002, Name = "铁剑" },
                },
                "Assets/Data/Configs/Test.csv");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_table);
        }

        [Test]
        public void Get_ReturnsRowById()
        {
            Assert.AreEqual("铁剑", _table.Get(1002).Name);
            Assert.AreEqual(2, _table.Count);
            Assert.AreEqual("Assets/Data/Configs/Test.csv", _table.SourceCsv);
        }

        [Test]
        public void Get_UnknownId_ReturnsNull()
        {
            Assert.IsNull(_table.Get(9999), "缺项返回 null 而不抛异常 —— 由调用方决定降级还是报错");
        }

        [Test]
        public void TryGet_And_Contains_ReportPresence()
        {
            Assert.IsTrue(_table.TryGet(1001, out TestRow row));
            Assert.AreEqual("木剑", row.Name);
            Assert.IsFalse(_table.TryGet(9999, out _));

            Assert.IsTrue(_table.Contains(1001));
            Assert.IsFalse(_table.Contains(9999));
        }
    }

    /// <summary>
    /// 生成代码的可用性（生成物已在仓库里，clone 即编译；这里验它的行为）。
    /// 注意：断言只覆盖仓库里真实存在的表 —— 示例数据目前只有 Item.csv；
    /// 若你新增了别的表，把对应的访问器也加进来即可（缺表时访问器返回 null，不会抛异常）。
    /// </summary>
    public class GeneratedConfigsTests
    {
        [Test]
        public void Configs_Accessors_NeverThrowWhenUnavailable()
        {
            // 未装配配置库（没跑导入 / 没挂 ConfigInstaller / 甚至 ConfigService 都没注册）时，
            // 访问器只返回 null，绝不抛异常 —— 让"配置没准备好"能在业务侧优雅降级。
            Assert.DoesNotThrow(() => { ItemTable item = Configs.Item; });
        }

        [Test]
        public void GeneratedTypes_MatchCsvContract()
        {
            Assert.IsTrue(typeof(IConfigRow).IsAssignableFrom(typeof(ItemConfig)), "行类型应实现 IConfigRow");
            Assert.AreEqual(typeof(ItemConfig), typeof(ItemTable).BaseType.GetGenericArguments()[0],
                "表类型应继承 ConfigTableBase<行类型>");
        }
    }
}
