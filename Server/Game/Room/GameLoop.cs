using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server;

public class GameLoop
{
    public List<Room> Rooms = new();

    private int _deltaTime = 50;

    public void Run()
    {
        while (true)
        {
            foreach (var room in Rooms)
            {
                room.Tick();
            }

            Thread.Sleep(_deltaTime);
        }
    }
}