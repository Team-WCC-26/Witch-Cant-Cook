using System;

public class Ingredient : IData
{
    public int id;
    public string name;
    public string statID;
    public string throwing;
    public string tag;
    public string exp;

    public int GetKey() => id;
    public Ingredient() { } 
}
