using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_CostumeChange)]
[PacketId(PacketId.S_CostumeChange)]
public partial class CostumeChangePacket
{
    public string? PlayerId { get; set; }
    public CostumeData Costume { get; set; }
}

/// <summary>
/// 플레이어 코스튬 데이터 (부위별 슬롯 번호, 부위별 가짓수는 160 이하)
/// </summary>
[MemoryPackable]
public readonly partial struct CostumeData : IEquatable<CostumeData>
{
    public readonly ushort Hair;
    public readonly ushort Hat;
    public readonly ushort Mustache;
    public readonly ushort Eyebrow;
    public readonly ushort Glasses;
    public readonly ushort Body;
    public readonly ushort Glove;
    public readonly ushort Backpack;
    public readonly ushort Outerwear;
    public readonly ushort Pants;
    public readonly ushort Shoe;
    public readonly ushort FullBody;

    public CostumeData(
        ushort hair = 0,
        ushort hat = 0,
        ushort mustache = 0,
        ushort eyebrow = 0,
        ushort glasses = 0,
        ushort body = 0,
        ushort glove = 0,
        ushort backpack = 0,
        ushort outerwear = 0,
        ushort pants = 0,
        ushort shoe = 0,
        ushort fullBody = 0)
    {
        Hair = hair;
        Hat = hat;
        Mustache = mustache;
        Eyebrow = eyebrow;
        Glasses = glasses;
        Body = body;
        Glove = glove;
        Backpack = backpack;
        Outerwear = outerwear;
        Pants = pants;
        Shoe = shoe;
        FullBody = fullBody;
    }

    public bool Equals(CostumeData other)
    {
        return Hair == other.Hair &&
               Hat == other.Hat &&
               Mustache == other.Mustache &&
               Eyebrow == other.Eyebrow &&
               Glasses == other.Glasses &&
               Body == other.Body &&
               Glove == other.Glove &&
               Backpack == other.Backpack &&
               Outerwear == other.Outerwear &&
               Pants == other.Pants &&
               Shoe == other.Shoe &&
               FullBody == other.FullBody;
    }
}
