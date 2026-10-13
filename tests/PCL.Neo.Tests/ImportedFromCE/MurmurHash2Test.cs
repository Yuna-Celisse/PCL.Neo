using System;
using Xunit;
using PCL.Neo.Core.Utils.Hash;
using PCL.Neo.Core.Utils.Exts;
using System.Text;

namespace PCL.Neo.Core.Test.Hash;


public class MurmurHash2Test
{
    [Fact]
    public void Test()
    {
        const string input = "aklerfdhvkore;fhjbgoiwrfgbuio34htgb889rguiyufgvueirefvrvu9vhgg9wygf94u8fgw249fyhuwygf293ghf8h";
        const uint output = 2672531333;
        var buf = Encoding.UTF8.GetBytes(input);
        var result = MurmurHash2Provider.Instance.ComputeHash(buf);
        Assert.Equal(output, BitConverter.ToUInt32(result));

        var hex = result.ToHexString().HexToBytes();
        Assert.Equal(output, BitConverter.ToUInt32(hex));
    }
}
