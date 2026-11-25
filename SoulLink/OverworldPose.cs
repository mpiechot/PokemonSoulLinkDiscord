namespace SoulLink;

public sealed record OverworldPose(
    int MapGroup, int MapNumber, int X, int Y, int OffsetX, int OffsetY,
    int Graphics, int Image, int Flip, int Elevation, int Priority, int Subsprite,
    int SurfImage = -1, int SurfOffsetY = 0, int SurfFlip = 0)
{
    public bool IsValid() =>
        this.MapGroup is >= 0 and <= 255 && this.MapNumber is >= 0 and <= 255 &&
        this.X is >= -32 and <= 16416 && this.Y is >= -32 and <= 16416 &&
        this.OffsetX is >= -64 and <= 64 && this.OffsetY is >= -64 and <= 64 &&
        this.Image >= 0 && this.Image < ImageCount(this.Graphics) && this.Flip is >= 0 and <= 3 &&
        this.Elevation is >= 0 and <= 15 && this.Priority is >= 0 and <= 3 && this.Subsprite is >= 0 and <= 5 &&
        this.SurfImage is >= -1 and <= 2 && this.SurfOffsetY is >= -32 and <= 32 && this.SurfFlip is >= 0 and <= 3 &&
        (this.SurfImage < 0 || this.Graphics is 2 or 92);

    private static int ImageCount(int graphics) => graphics switch
    {
        0 or 89 => 18,
        63 or 91 => 27,
        2 or 92 or 137 or 138 => 12,
        1 or 90 or 111 or 112 or 191 or 192 => 9,
        3 or 93 => 5,
        193 or 194 => 1,
        _ => 0,
    };
}

public sealed record OverworldPlayer(Guid PlayerId, OverworldPose Pose);
