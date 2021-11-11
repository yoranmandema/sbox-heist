using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox;

/*
 * NpcPoints are helper points on a navmesh that contain info used in tactical decision-making.
 * The class stores a list of visibility info, pre-generated world traces in 8 directions for choosing positions.
 */
public class NpcPoint
{

	[ConVar.Replicated]
	public static int heist_navpoints_draw { get; set; } = 0;

	public static List<NpcPoint> All = new List<NpcPoint>();

	public static float MaxVisDistance = 3000f;
	public static int PolarDivs = 12;

	Vector3 Position;
	public List<float> PolarVisInfo { get; protected set; }
	public static readonly List<float> PolarHeight = new List<float>() { 32f, 64f };

	Entity Claimant;

	public static NpcPoint CreatePoint(Vector3 Position )
	{
		var p = new NpcPoint( Position );
		All.Add( p );
		return p;
	}

	public NpcPoint(Vector3 Position)
	{
		this.Position = Position;
		GenerateVisInfo();
	}

	public Vector3 GetPosition()
	{
		return Position;
	}

	public bool IsClaimed()
	{
		return Claimant != null && Claimant.IsValid();
	}
	public Entity GetClaimant()
	{
		return Claimant;
	}
	public bool Claim( Entity ent )
	{
		if ( IsClaimed() ) return false;
		Claimant = ent;

		return true;
	}
	public void Unclaim( Entity ent )
	{
		if (Claimant == ent)
		{
			Claimant = null;
		}
	}

	public void GenerateVisInfo()
	{
		PolarVisInfo = new List<float>();
		
		// For each height we want (crouch and standing)...
		for (int j = 0; j < PolarHeight.Count; j++ )
		{
			var f = PolarHeight[j];
			var vec = new Vector3( 0, 0, f );
			for ( int i = 0; i < PolarDivs; i++ )
			{
				// For each of the directions...
				var dir = Rotation.FromYaw( 360 / PolarDivs * i ).Forward;
				// Run a trace against the world and save its distance
				var tr = Trace.Ray( Position + vec, Position + vec + dir * MaxVisDistance )
					.WorldOnly()
					.Run();
				PolarVisInfo.Add( tr.Distance );
			}
		}
	}
	public List<bool> Visible(Vector3 pos)
	{
		var vec = pos - Position;
		// Use two angles, with a small bit of offset, if the actual angle is somewhere in the middle of two polar traces
		var i = (int)(Math.Round( (vec.EulerAngles.yaw + 10f) / (360 / PolarDivs) ) % PolarDivs);
		var i2 = (int)(Math.Round( (vec.EulerAngles.yaw - 10f) / (360 / PolarDivs) ) % PolarDivs);
		var list = new List<bool>();
		for (int j = 0; j < PolarHeight.Count; j++ )
		{
			list.Add( MathF.Min( PolarVisInfo[j * PolarDivs + i], PolarVisInfo[j * PolarDivs + i2] ) >= vec.Length );
		}
		return list;
	}

	public List<float> VisDistTo( Vector3 pos )
	{
		var vec = pos - Position;
		// Use two angles, with a small bit of offset, if the actual angle is somewhere in the middle of two polar traces
		var i = (int)(Math.Round( (vec.EulerAngles.yaw + 10f) / (360 / PolarDivs) ) % PolarDivs);
		var i2 = (int)(Math.Round( (vec.EulerAngles.yaw - 10f) / (360 / PolarDivs) ) % PolarDivs);
		var list = new List<float>();
		for ( int j = 0; j < PolarHeight.Count; j++ )
		{
			list.Add( MathF.Min( PolarVisInfo[j * PolarDivs + i], PolarVisInfo[j * PolarDivs + i2] ) );
		}
		return list;
	}

	public void DebugDraw( float time, float opacity = 1.0f )
	{
		var draw = Sandbox.Debug.Draw.ForSeconds( time );
		var lift = Vector3.Up * 2;

		if (IsClaimed())
		{
			draw.WithColor( Color.Orange.WithAlpha( opacity ) ).Circle( lift + Position, Vector3.Up, 20.0f );
			draw.WithColor( Color.Orange.WithAlpha( opacity ) ).Line( Position, Position + Vector3.Up * PolarHeight[PolarHeight.Count() - 1] );
		} else
		{
			draw.WithColor( Color.White.WithAlpha( opacity ) ).Circle( lift + Position, Vector3.Up, 20.0f );
			draw.WithColor( Color.White.WithAlpha( opacity ) ).Line( Position, Position + Vector3.Up * PolarHeight[PolarHeight.Count() - 1] );
		}

		if ( heist_navpoints_draw >= 2 )
			for ( int j = 0; j < PolarHeight.Count; j++ )
			{
				var f = PolarHeight[j];
				var vec = new Vector3( 0, 0, f );
				for ( int i = 0; i < PolarDivs; i++ )
				{
					var dir = Rotation.FromYaw( 360 / PolarDivs * i ).Forward;
					var dist = PolarVisInfo[(j * PolarDivs) + i];
					if (MathF.Abs( MaxVisDistance - dist ) < 0.001f)
					{
						draw.WithColor( Color.Green.WithAlpha( opacity ) ).Line( Position + vec, Position + vec + dir * 150f );
					} else
					{
						draw.WithColor( Color.Red.WithAlpha( opacity ) ).Line( Position + vec, Position + vec + dir * dist );
					}
				}
			}
	}

	[ServerCmd( "heist_navpoints_save" )]
	public static void SaveToFile()
	{
		FileSystem.Data.CreateDirectory( "heist" );

		List<Vector3> pointpos = new();
		foreach (var p in All)
		{
			pointpos.Add( p.GetPosition() );
		}

		FileSystem.Data.WriteJson("heist/" + Global.MapName + "_navpoints.json", pointpos );
		Log.Info( "Saved " + All.Count() + " nav points to file." );
	}

	[ServerCmd( "heist_navpoints_load" )]
	public static void LoadFromFile()
	{
		if ( FileSystem.Data.FileExists( "heist/" + Global.MapName + "_navpoints.json" ) )
		{
			All.Clear();
			var pointpos = FileSystem.Data.ReadJson<List<Vector3>>( "heist/" + Global.MapName + "_navpoints.json" );
			foreach (var p in pointpos)
			{
				CreatePoint( p );
			}
			Log.Info( "Generated " + All.Count() + " nav points from file." );
		} else
		{
			Log.Info( "No saved nav points exist." );
		}
	}

	public static void DrawPoints()
	{
		if ( heist_navpoints_draw > 0 )
		{
			var arr = All;
			foreach ( var p in arr )
			{
				p.DebugDraw( 0.1f, 0.2f );
			}
		}
	}
}
