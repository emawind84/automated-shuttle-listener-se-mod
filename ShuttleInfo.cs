using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRageMath;

namespace IngameScript
{
    struct ShuttleInfo
    {
        public long ID { get; set; }
        public string Name { get; set; }
        public Vector3D Position { get; set; }
        public string Message { get; set; }
        public DateTime Created { get; set; }

        public bool IsRecent => DateTime.Now - this.Created < new TimeSpan(0, 0, 30);

        public override string ToString()
        {
            return $"{this.Name}" + Environment.NewLine +
                $"Position: {this.Position}" + Environment.NewLine +
                $"Msg: {this.Message}";
        }
    }
}
