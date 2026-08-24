using UnityEngine;

public class Define
{
    public enum eIngredient
    {
        Mushroom = 10100,
        Broccoli = 10200,
        Potato = 10300,
        Cheese = 10400,
        Basil = 10500,
        Carrot = 10600,
        Shrimp = 10700,
        Flour = 10800,
        Tomato = 10900,
        Onion = 11000,
        Salmon = 11100,
        Honey = 11200,
        MandragoraRoot = 11300,
        Salt = 11400,
        Sugar = 11500,
        Parsley = 11600,
        Butter = 11700,
        Egg = 11800,
        Corn = 11900,
        Fish = 12000,
        Cabbage = 12100,
        Meat = 12200,
        Squid = 12300,
        Popcorn = 12400,
        OnionLiquid = 50001,
        HoneyLiquid = 50002,
        EggInside = 50003,
        Trash = 99999
    }

    public enum eToolId
    {
        KitchenKnife = 10,
        FryingPan = 20,
        Stove = 30,
        Pot = 40,
        Plate = 50,
        PrepTable = 60,
        Oven = 80,
    }

    public static bool TryGetToolId(CatchableObjType objType, out eToolId toolId)
    {
        switch (objType)
        {
            case CatchableObjType.Knife:
                toolId = eToolId.KitchenKnife;
                return true;
            case CatchableObjType.Pan:
                toolId = eToolId.FryingPan;
                return true;
            case CatchableObjType.Plate:
                toolId = eToolId.Plate;
                return true;
            default:
                toolId = default;
                return false;
        }
    }

    public static bool TryGetCatchableObjType(eToolId toolId, out CatchableObjType objType)
    {
        switch (toolId)
        {
            case eToolId.KitchenKnife:
                objType = CatchableObjType.Knife;
                return true;
            case eToolId.FryingPan:
                objType = CatchableObjType.Pan;
                return true;
            case eToolId.Plate:
                objType = CatchableObjType.Plate;
                return true;
            default:
                objType = CatchableObjType.Default;
                return false;
        }
    }
}

