using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server;

public class Oven(int toolId) : CookingTool(toolId)
{
    protected override void Cook(CookingTool tool)
    {
        throw new NotImplementedException();
    }
}
