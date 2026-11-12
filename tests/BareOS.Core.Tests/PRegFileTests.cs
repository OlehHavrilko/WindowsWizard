using BareOS.Core.Windows;
using Xunit;

namespace BareOS.Core.Tests;

/// <summary>Чистые функции формата PReg (Registry.pol), без Windows (D-031).</summary>
public class PRegFileTests
{
    [Fact]
    public void RoundTrip_SingleDwordEntry()
    {
        var entries = new[] { new PRegFile.Entry(@"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 4u, BitConverter.GetBytes(0u)) };
        var bytes = PRegFile.Serialize(entries);
        var back = PRegFile.Parse(bytes);
        Assert.Single(back);
        Assert.Equal(entries[0].Key, back[0].Key);
        Assert.Equal(entries[0].Value, back[0].Value);
        Assert.Equal(entries[0].Type, back[0].Type);
        Assert.Equal(entries[0].Data, back[0].Data);
    }

    [Fact]
    public void RoundTrip_MultipleEntries_PreservesOrder()
    {
        var entries = new[]
        {
            new PRegFile.Entry(@"SOFTWARE\A", "X", 1u, new byte[] { 0, 0 }),
            new PRegFile.Entry(@"SOFTWARE\B", "**del.Y", 1u, new byte[] { 0, 0 }),
            new PRegFile.Entry(@"SOFTWARE\C", "Z", 11u, BitConverter.GetBytes(123UL)),
        };
        var back = PRegFile.Parse(PRegFile.Serialize(entries));
        Assert.Equal(entries.Length, back.Count);
        for (int i = 0; i < entries.Length; i++)
        {
            Assert.Equal(entries[i].Key, back[i].Key);
            Assert.Equal(entries[i].Value, back[i].Value);
            Assert.Equal(entries[i].Data, back[i].Data);
        }
    }

    [Fact]
    public void Parse_Empty_ReturnsNoEntries()
        => Assert.Empty(PRegFile.Parse(PRegFile.Serialize(Array.Empty<PRegFile.Entry>())));

    [Fact]
    public void Parse_TooShort_ReturnsNoEntries()
        => Assert.Empty(PRegFile.Parse(new byte[] { 1, 2, 3 }));

    [Fact]
    public void Parse_BadSignature_Throws()
        => Assert.Throws<FormatException>(() => PRegFile.Parse(new byte[] { 0, 0, 0, 0, 1, 0, 0, 0 }));
}
