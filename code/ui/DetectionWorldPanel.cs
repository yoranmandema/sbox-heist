using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox;
using Sandbox.UI;
using Sandbox.UI.Construct;

class DetectionWorldPanel : WorldPanel
{
	public DetectionWorldPanel()
	{
		StyleSheet.Load( "/ui/DetectionWorldPanel.scss" );
		Add.Label( "hello world" );
	}
}
