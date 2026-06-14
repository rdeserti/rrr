using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace rrr.Scene
{
    public abstract class Light
    {
        /// <summary>
        /// Whether this light projects shadows (a shadow map is rendered for
        /// it). Effective only when shadows are enabled in RenderSettings.
        /// </summary>
        public bool CastsShadows = true;
    }
}
