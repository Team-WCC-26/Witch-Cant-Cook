using System;

[Flags]
public enum EntityCategory
{
    None = 0,
    Player = 1 << 0,
    Ingredient = 1 << 1,
    Pan = 1 << 2,
    Knife = 1 << 3,
    Plate = 1 << 4,
    Broom = 1 << 5,
    Bucket = 1 << 6,
    Stove = 1 << 7,
    PrepTable = 1 << 8,
    Oven = 1 << 9,
    Pot = 1 << 10,

    All = ~0
}
