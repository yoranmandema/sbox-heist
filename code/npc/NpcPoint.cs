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
	public static bool nav_drawpoints { get; set; } = false;

	public static List<NpcPoint> All = new List<NpcPoint>();

	public static float MaxVisDistance = 1000f;

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
		return Claimant.IsValid();
	}

	public bool Claim( Entity ent )
	{
		if ( IsClaimed() ) return false;
		Claimant = ent;

		return true;
	}

	public void GenerateVisInfo()
	{
		PolarVisInfo = new List<float>();
		
		// For each height we want (crouch and standing)...
		for (int j = 0; j < PolarHeight.Count; j++ )
		{
			var f = PolarHeight[j];
			var vec = new Vector3( 0, 0, f );
			for ( int i = 0; i < 8; i++ )
			{
				// For each of the 8 directions...
				var dir = Rotation.FromYaw( 360 / 8 * i ).Forward;
				// Run a trace against the world and save its distance
				var tr = Trace.Ray( Position + vec, Position + vec + dir * MaxVisDistance )
					.WorldOnly()
					.Run();
				Log.Info( (j * 8) + i + " " + tr.Distance );
				PolarVisInfo.Add( tr.Distance );
				//PolarVisInfo.Insert( (j + 1) * i, tr.Distance );
			}
		}
	}

	public virtual void Tick()
	{
		if ( nav_drawpoints )
		{
			using ( Sandbox.Debug.Profile.Scope( "Update Path" ) )
			{
				DebugDraw(0.1f, 0.5f);
			}
		}

	}
	public void DebugDraw( float time, float opacity = 1.0f )
	{
		var draw = Sandbox.Debug.Draw.ForSeconds( time );
		var lift = Vector3.Up * 2;

		draw.WithColor( Color.White.WithAlpha( opacity ) ).Circle( lift + Position, Vector3.Up, 20.0f );

		draw.WithColor( Color.White.WithAlpha( opacity ) ).Line( Position, Position + Vector3.Up * PolarHeight[PolarHeight.Count() - 1] );
		for ( int j = 0; j < PolarHeight.Count; j++ )
		{
			var f = PolarHeight[j];
			var vec = new Vector3( 0, 0, f );
			for ( int i = 0; i < 8; i++ )
			{
				var dir = Rotation.FromYaw( 360 / 8 * i ).Forward;
				var dist = PolarVisInfo[(j * 8) + i];
				if (MathF.Abs( MaxVisDistance - dist ) < 0.001f)
				{
					draw.WithColor( Color.Green.WithAlpha( opacity ) ).Line( Position + vec, Position + vec + dir * dist );
				} else
				{
					draw.WithColor( Color.Red.WithAlpha( opacity ) ).Line( Position + vec, Position + vec + dir * dist );
				}
			}
		}
	}
}
