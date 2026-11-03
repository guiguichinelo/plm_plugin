using System.Buffers.Binary;
namespace Plm.Core;
public static class JpegThumbnail
{
    public static void Validate(byte[] data)
    {
        if(data is null||data.Length is <16 or >524288||data[0]!=0xff||data[1]!=0xd8||data[^2]!=0xff||data[^1]!=0xd9)throw new ArgumentException("Invalid JPEG thumbnail.");
        var offset=2;
        while(offset+4<data.Length){
            if(data[offset++]!=0xff)throw new ArgumentException("Invalid JPEG marker.");while(offset<data.Length&&data[offset]==0xff)offset++;if(offset>=data.Length)break;var marker=data[offset++];
            if(marker is 0xda or 0xd9)break;if(marker is >=0xd0 and <=0xd7||marker==0x01)continue;
            var length=BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset,2));if(length<2||offset+length>data.Length)throw new ArgumentException("Invalid JPEG segment.");
            if(marker is 0xc0 or 0xc1 or 0xc2){if(length<8)break;var height=BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset+3,2));var width=BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset+5,2));if(width is <1 or >2048||height is <1 or >2048)throw new ArgumentException("Thumbnail dimensions exceed the limit.");return;}
            offset+=length;
        }throw new ArgumentException("Missing JPEG dimensions.");
    }
}
