using Sandbox;
using System;
using System.Linq;

enum HeistGameState
{
	Waiting,
	Pregame,
	Active,
	Postgame
}

/// <summary>
/// This is the heart of the gamemode. It's responsible
/// for creating the player and stuff.
/// </summary>
[Library( "heist", Title = "Heist" )]
partial class HeistGame : Game
{
	public static HeistGameState GameState { get; protected set; }

	public HeistGame()
	{
		if ( IsServer )
		{
			new HeistHud();
		}

		GameState = HeistGameState.Waiting;
	}

	public override void PostLevelLoaded()
	{
		base.PostLevelLoaded();
		NpcPoint.LoadFromFile();
	}

	public override void ClientJoined( Client cl )
	{
		base.ClientJoined( cl );

		var player = new HeistPlayer();
		player.UpdateClothes( cl );
		player.Respawn();

		cl.Pawn = player;
	}

	public override void Simulate( Client cl )
	{
		base.Simulate( cl );

		NpcPoint.DrawPoints();
	}
}
