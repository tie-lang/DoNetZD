using DoNetZD;
using Xunit;

namespace DoNetZD.Tests;

/// <summary>路径访问 / 深度比较 / 合并 / 遍历。</summary>
public class QueryTests
{
    private static ZdValue Root()
    {
        var d = new Dictionary<string, ZdValue>
        {
            ["server"] = new ZdValue.Map(new Dictionary<string, ZdValue>
            {
                ["host"] = new ZdValue.String("0.0.0.0"),
                ["port"] = new ZdValue.Integer(8080),
            }),
            ["users"] = new ZdValue.Array(new ZdValue[]
            {
                new ZdValue.Map(new Dictionary<string, ZdValue>
                {
                    ["name"] = new ZdValue.String("张三"),
                    ["age"] = new ZdValue.Integer(30),
                }),
                new ZdValue.Map(new Dictionary<string, ZdValue>
                {
                    ["name"] = new ZdValue.String("李四"),
                    ["age"] = new ZdValue.Integer(25),
                }),
            }),
        };
        return new ZdValue.Map(d);
    }

    // ---- ZdPath ----

    [Fact]
    public void Path_Get_DotAndIndex()
    {
        ZdValue r = Root();
        Assert.Equal("0.0.0.0", ((ZdValue.String)ZdPath.Get(r, "server.host")!).Value);
        Assert.Equal(8080L, ((ZdValue.Integer)ZdPath.Get(r, "server.port")!).Value);
        Assert.Equal("李四", ((ZdValue.String)ZdPath.Get(r, "users[1].name")!).Value);
        Assert.Equal(30L, ((ZdValue.Integer)ZdPath.Get(r, "users[0].age")!).Value);
    }

    [Fact]
    public void Path_Missing_ReturnsNull()
    {
        ZdValue r = Root();
        Assert.Null(ZdPath.Get(r, "server.missing"));
        Assert.Null(ZdPath.Get(r, "users[9].name"));
        Assert.Null(ZdPath.Get(r, "nope.nada"));
        Assert.False(ZdPath.TryGet(r, "users[0].xx", out _));
    }

    [Fact]
    public void Path_DollarPrefix_AndQuotedKey()
    {
        ZdValue r = Root();
        Assert.Equal("张三", ((ZdValue.String)ZdPath.Get(r, "$.users[0].name")!).Value);
        Assert.Equal(8080L, ((ZdValue.Integer)ZdPath.Get(r, "['server'].port")!).Value);
    }

    // ---- DeepEquals / GetHashCode ----

    [Fact]
    public void DeepEquals_True_False()
    {
        ZdValue r1 = Root();
        ZdValue r2 = Root();
        Assert.True(r1.DeepEquals(r2));
        Assert.Equal(r1.GetHashCode(), r2.GetHashCode());

        // 类型不同 → 不等
        Assert.False(r1.DeepEquals(new ZdValue.Integer(1)));
        Assert.False(r1.DeepEquals(null));
        Assert.True(r1.DeepEquals(r1));  // 同引用
    }

    [Fact]
    public void DeepEquals_Containers()
    {
        var a = new ZdValue.Array(new ZdValue[] { new ZdValue.Integer(1), new ZdValue.Integer(2) });
        var b = new ZdValue.Array(new ZdValue[] { new ZdValue.Integer(1), new ZdValue.Integer(2) });
        Assert.True(a.DeepEquals(b));
        Assert.False(a.DeepEquals(new ZdValue.Array(new ZdValue[] { new ZdValue.Integer(1) })));
    }

    // ---- Merge (RFC 7396) ----

    [Fact]
    public void Merge_AddReplaceRemove()
    {
        ZdValue base_ = Root();
        var patch = new ZdValue.Map(new Dictionary<string, ZdValue>
        {
            ["server"] = new ZdValue.Map(new Dictionary<string, ZdValue>
            {
                ["host"] = new ZdValue.String("127.0.0.1"),       // 替换
                ["port"] = new ZdValue.Integer(9000),              // 替换（同为 map 时递归，host/port 被覆盖）
            }),
            ["users"] = ZdValue.Null.Instance,                        // 删除
            ["new_key"] = new ZdValue.Integer(1),                  // 新增
        });

        ZdValue merged = base_.Merge(patch);
        var m = (ZdValue.Map)merged;
        Assert.Equal("127.0.0.1", ((ZdValue.String)((ZdValue.Map)m.Entries["server"]).Entries["host"]).Value);
        Assert.Equal(9000L, ((ZdValue.Integer)((ZdValue.Map)m.Entries["server"]).Entries["port"]).Value);
        Assert.False(m.Entries.ContainsKey("users"));             // 被删除
        Assert.True(m.Entries.ContainsKey("new_key"));            // 新增
        Assert.True(((ZdValue.Map)base_).Entries.ContainsKey("users"));          // 原值不变（不可变）
    }

    [Fact]
    public void Merge_NonMap_ReplacesWhole()
    {
        ZdValue base_ = Root();
        ZdValue merged = base_.Merge(new ZdValue.Integer(0));
        Assert.IsType<ZdValue.Integer>(merged);
    }

    // ---- Visit ----

    [Fact]
    public void Visit_CountsAllNodes()
    {
        ZdValue r = Root();
        int count = 0;
        r.Visit(_ => count++);
        // 根 map + server map + host/port + users array + 2 user map + 4 标量 = 11
        Assert.Equal(11, count);
    }
}
