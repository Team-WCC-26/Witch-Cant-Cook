//namespace Server;

//public class CookManager
//{
//    private Dictionary<long, CookingTool> _cookingTools = new();
//    private HashSet<long> _processingTools = new();

//    public void Tick(long deltaTime)
//    {
//        foreach (var toolId in  _processingTools)
//        {
//            _cookingTools[toolId].Tick(deltaTime);
//        }
//    }
    
//    public void Init()
//    {
//        _cookingTools.Clear();
//    }

//    public void RegisterCookingTool(long id, CookingTool tool)
//    {
//        _cookingTools.Add(id, tool);
//    }

//    public bool TryCook(long toolId, Ingredient ingredient)
//    {
//        if (!_cookingTools.TryGetValue(toolId, out var cookingTool)) return false;
//        if (!cookingTool.TryStartCook(ingredient, () =>
//        {
//            _processingTools.Remove(toolId);
//        })) return false;

//        _processingTools.Add(toolId);

//        return true;
//    }
//}
