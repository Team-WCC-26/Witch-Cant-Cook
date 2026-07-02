using Newtonsoft.Json;
using Protocol;

namespace Server;

public class DataBase
{
    public IReadOnlyDictionary<int, IngredientData> Ingredients => _ingredients;
    public IReadOnlyDictionary<int, IngredientStatData> IngredientStats => _ingredientStats;
    public IReadOnlyDictionary<RecipeKey, int> IngredientCombinations => _ingredientCombinations;
    public IReadOnlyDictionary<int, ToolData> Tools => _tools;
    public IReadOnlyDictionary<int, DishData> Dishes => _dishes;

    private readonly Dictionary<int, IngredientData> _ingredients = new();
    private readonly Dictionary<int, IngredientStatData> _ingredientStats = new();
    private readonly Dictionary<RecipeKey, int> _ingredientCombinations = new();
    private readonly Dictionary<int, ToolData> _tools = new();
    private readonly Dictionary<int, DishData> _dishes = new();
    //private readonly Dictionary<IngredientStatePair, HashSet<RecipeKey>> _recipeCandidate = new();

    public async Task Init()
    {
        Console.WriteLine("DataBase Initilizing...");

        using (HttpClient client = new())
        {
            string url = "https://script.google.com/macros/s/AKfycbzTL3tVHIradyC9ZqIlz5agPNYQIhtxsQUYsWCxlvweYUPtpdaZEPfMzL8budqDN-t4/exec";
            string export = "?exportSheet=";
            string ingredient = "Ingredient";
            string stat = "Stat";
            string combination = "Combination";
            string tool = "Tool";
            string dish = "Recipe";

            // 재료 데이터 파싱
            string json = await client.GetStringAsync(url + export + ingredient);

            _ingredients.Clear();

            foreach (var ingredientData in JsonConvert.DeserializeObject<List<IngredientData>>(json))
            {
                _ingredients[ingredientData.Id] = ingredientData;
            }

            // 재료 스탯 데이터 파싱
            json = await client.GetStringAsync(url + export + ingredient + stat);

            _ingredientStats.Clear();

            foreach (var ingredientStatData in JsonConvert.DeserializeObject<List<IngredientStatData>>(json))
            {
                _ingredientStats[ingredientStatData.Id] = ingredientStatData;
            }

            // 재료 조합 데이터 파싱
            json = await client.GetStringAsync(url + export + ingredient + combination);
            BuildRecipeData(JsonConvert.DeserializeObject<List<IngredientCombinationData>>(json));

            // 도구 데이터 파싱
            json = await client.GetStringAsync(url + export + tool);

            _tools.Clear();

            foreach (var tooData in JsonConvert.DeserializeObject<List<ToolData>>(json))
            {
                _tools[tooData.Id] = tooData;
            }

            // 최종 요리 데이터 파싱
            json = await client.GetStringAsync(url + export + dish);

            _dishes.Clear();

            foreach (var dishData in JsonConvert.DeserializeObject<List<DishData>>(json))
            {
                _dishes[dishData.Id] = dishData;
            }
        };

        Console.WriteLine("DataBase Initilizing Complete!");
    }

    public bool TryGetIngredientStatById(int ingredientId, out IngredientStatData? statData)
    {
        statData = null;

        if (!Ingredients.TryGetValue(ingredientId, out var ingredient)) return false;
        if (!IngredientStats.TryGetValue(ingredient.StatId, out statData)) return false;

        return true;
    }

    public bool CheckCookEnable(int ingredientId, IngredientState state)
    {
        return (Ingredients[ingredientId].InvalidProcessFlag & state) != 0;
    }

    /*
    public bool TryGetRecipeCandiates(IngredientStatePair ingredient, out IReadOnlySet<RecipeKey> candidates)
    {
        if (_recipeCandidate.TryGetValue(ingredient, out var set))
        {
            candidates = set;
            return true;
        }

        candidates = null!;
        return false;
    }
    

    public bool CheckRecipeValid(IEnumerable<IngredientStatePair> ingredients)
    {
        var ingredientArray = ingredients.ToArray();

        if (ingredientArray.Length == 0) return false;

        List<HashSet<RecipeKey>> candidateSets = new(); // GC Spike 생기면 ArrayPool등으로 수정

        foreach (var ingredient in ingredientArray)
        {
            if (!_recipeCandidate.TryGetValue(ingredient, out var set)) return false;

            candidateSets.Add(set);
        }

        candidateSets.Sort((a, b) => a.Count.CompareTo(b.Count));

        HashSet<RecipeKey> result = new(candidateSets[0]);

        for (int i = 1; i < candidateSets.Count; i++)
        {
            result.IntersectWith(candidateSets[i]);

            if (result.Count == 0) return false;
        }

        return result.Count > 0;
    }
    */

    private void BuildRecipeData(List<IngredientCombinationData> combinationList)
    {
        _ingredientCombinations.Clear();
        //_recipeCandidate.Clear();

        var grouped = combinationList.GroupBy(x => x.ResultId);

        foreach (var group in grouped)
        {
            int resuldId = group.Key;
            IngredientStatePair[] ingredients = group.Select(x => new IngredientStatePair(x.IngredientId, x.ConditionFlag)).ToArray();

            RecipeKey recipeKey = new RecipeKey(ingredients);

            _ingredientCombinations[recipeKey] = resuldId;

            /*
            foreach (var ingredient in ingredients)
            {
                if (!_recipeCandidate.TryGetValue(ingredient, out var candidateSet))
                {
                    candidateSet = new();
                    _recipeCandidate[ingredient] = candidateSet;
                }

                candidateSet.Add(recipeKey);
            }
            */
        }
    }
}
