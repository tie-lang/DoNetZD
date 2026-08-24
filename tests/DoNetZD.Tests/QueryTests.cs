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

    // ---- Set / TrySet（路径写回）----

    [Fact]
    public void Set_ReplaceLeaf()
    {
        ZdValue r = Root();
        ZdValue updated = ZdPath.Set(r, "server.port", new ZdValue.Integer(9090));
        Assert.Equal(9090L, ((ZdValue.Integer)ZdPath.Get(updated, "server.port")!).Value);
        // 原根不变（不可变）
        Assert.Equal(8080L, ((ZdValue.Integer)ZdPath.Get(r, "server.port")!).Value);
    }

    [Fact]
    public void Set_AddNewKey_AndStoreBack()
    {
        ZdValue r = Root();
        r = ZdPath.Set(r, "server.extra", new ZdValue.String("x"));   // 写回
        Assert.Equal("x", ((ZdValue.String)ZdPath.Get(r, "server.extra")!).Value);
    }

    [Fact]
    public void Set_CreatesIntermediateMaps()
    {
        ZdValue r = Root();
        ZdValue updated = ZdPath.Set(r, "cache.ttl", new ZdValue.Integer(600));
        Assert.Equal(600L, ((ZdValue.Integer)ZdPath.Get(updated, "cache.ttl")!).Value);
        // 中间 cache 键被自动创建
        Assert.True(((ZdValue.Map)ZdPath.Get(updated, "cache")!).Entries.ContainsKey("ttl"));
        // 原根无 cache
        Assert.Null(ZdPath.Get(r, "cache"));
    }

    [Fact]
    public void Set_ReplaceArrayElement()
    {
        ZdValue r = Root();
        ZdValue updated = ZdPath.Set(r, "users[0].age", new ZdValue.Integer(99));
        Assert.Equal(99L, ((ZdValue.Integer)ZdPath.Get(updated, "users[0].age")!).Value);
        Assert.Equal(30L, ((ZdValue.Integer)ZdPath.Get(r, "users[0].age")!).Value);
    }

    [Fact]
    public void Set_AppendToArray_AtCountIndex()
    {
        var r = new ZdValue.Map(new Dictionary<string, ZdValue>
        {
            ["arr"] = new ZdValue.Array(new ZdValue[] { new ZdValue.Integer(1), new ZdValue.Integer(2) }),
        });
        ZdValue updated = ZdPath.Set(r, "arr[2]", new ZdValue.Integer(3));
        var arr = (ZdValue.Array)ZdPath.Get(updated, "arr")!;
        Assert.Equal(3, arr.Items.Count);
        Assert.Equal(3L, ((ZdValue.Integer)arr.Items[2]).Value);
    }

    [Fact]
    public void TrySet_Failures()
    {
        ZdValue r = Root();
        // 数组叶子越界（i > Count）
        Assert.False(ZdPath.TrySet(r, "users[9].name", new ZdValue.String("x"), out _));
        // 中间数组越界
        Assert.False(ZdPath.TrySet(r, "users[5].x", new ZdValue.String("x"), out _));
        // 段类型不匹配：Key 落在 Array 上
        Assert.False(ZdPath.TrySet(r, "users.name", new ZdValue.String("x"), out _));
        // 段类型不匹配：Index 落在 Map 上
        Assert.False(ZdPath.TrySet(r, "server[0]", new ZdValue.String("x"), out _));
        // 非容器（标量）路径上继续下钻
        Assert.False(ZdPath.TrySet(r, "server.port.deep", new ZdValue.String("x"), out _));
    }

    [Fact]
    public void Set_BuildTreeFromNullRoot()
    {
        // 纯 Key 链：null 根也能建链（根自动成为 Map）
        Assert.True(ZdPath.TrySet(null!, "a.b.c", new ZdValue.Integer(1), out ZdValue? root));
        Assert.Equal(1L, ((ZdValue.Integer)ZdPath.Get(root!, "a.b.c")!).Value);
    }

    [Fact]
    public void Set_EmptyPath_ReplacesRoot()
    {
        ZdValue v = new ZdValue.Integer(7);
        Assert.Same(v, ZdPath.Set(Root(), "", v));
    }

    [Fact]
    public void Set_Throws_OnUnwritablePath()
    {
        Assert.Throws<InvalidOperationException>(() => ZdPath.Set(Root(), "users[9]", new ZdValue.Integer(1)));
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
