﻿using Sandbox;
using System;
using System.Linq;

partial class HeistViewModel : BaseViewModel
{
	float walkBob = 0;
	Vector3 velocity;
	Vector3 force;
	Vector3 acceleration;
	float MouseScale => 1.5f;
	float ReturnForce => 400f;
	float Damping => 18f;
	float AccelDamping => 0.05f;
	float PivotForce => 1000f;
	float VelocityScale => 10f;
	float RotationScale => 2.5f;
	float LookUpPitchScale => 70f;
	float LookUpSpeedScale => 80f;
	float UpDownDamping => 10f;
	float NoiseSpeed => 0.8f;
	float NoiseScale => 20f;

	Vector3 WalkCycleOffsets => new Vector3( 50f, 20f, 20f );
	float ForwardBobbing => 3f;
	float SideWalkOffset => 80f;
	Vector3 Offset => new Vector3( 1f, 13f, -8f );
	Vector3 CrouchOffset => new Vector3( -10f, -50f, -0f );
	Vector3 SmoothedVelocity;
	float VelocityClamp => 3f;

	float noiseZ = 0;
	float noisePos = 0;
	float upDownOffset = 0;
	float avoidance = 0;

	float sprintLerp = 0;

	public HeistViewModel()
	{
		noiseZ = Rand.Float( -10000, 10000 );
	}

	public override void PostCameraSetup( ref CameraSetup camSetup )
	{
		base.PostCameraSetup( ref camSetup );

		AddCameraEffects( ref camSetup );
	}

	private void AddCameraEffects( ref CameraSetup camSetup )
	{
		SmoothedVelocity += (Owner.Velocity - SmoothedVelocity) * 5f * Time.Delta;

		var speed = Owner.Velocity.Length.LerpInverse( 0, 320 );
		var bobSpeed = SmoothedVelocity.Length.LerpInverse( -100, 320 );
		var left = camSetup.Rotation.Left;
		var up = camSetup.Rotation.Up;
		var forward = camSetup.Rotation.Forward;
		var owner = Owner as HeistPlayer;
		var walkController = owner.Controller as WalkController;
		var avoidanceTrace = Trace.Ray( camSetup.Position, camSetup.Position + forward * 50f )
						.UseHitboxes()
						.Ignore( Owner )
						.Ignore( this )
						.Run();

		LerpTowards( ref avoidance, avoidanceTrace.Hit ? (1f - avoidanceTrace.Fraction) : 0, 10f );
		LerpTowards( ref sprintLerp, walkController?.Input.Down( InputButton.Run ) == true ? 1 : 0, 8f );

		bobSpeed *= (1 - sprintLerp * 0.25f);

		if ( Owner.GroundEntity != null )
		{
			walkBob += Time.Delta * 30.0f * bobSpeed;
		}

		if ( Owner.Velocity.Length < 60 )
		{
			var step = MathF.Round( walkBob / 90 );

			walkBob += (step * 90 - walkBob) * 10f * Time.Delta;
		}

		if ( walkController?.Duck?.IsActive == true )
		{
			acceleration += CrouchOffset * Time.Delta;
		}

		walkBob %= 360;

		upDownOffset += speed * -LookUpSpeedScale * Time.Delta;
		upDownOffset += camSetup.Rotation.Forward.z * -LookUpPitchScale * Time.Delta;

		ApplyDamping( ref upDownOffset, UpDownDamping );

		noisePos += Time.Delta * NoiseSpeed;

		acceleration += Vector3.Left * -Local.Client.Input.MouseDelta.x * Time.Delta * MouseScale;
		acceleration += Vector3.Up * -Local.Client.Input.MouseDelta.y * Time.Delta * MouseScale;
		acceleration += -velocity * ReturnForce * Time.Delta;

		// Apply horizontal offsets based on walking direction
		var horizontalForwardBob = WalkCycle( 0.5f, 3f ) * speed * WalkCycleOffsets.x * Time.Delta;

		acceleration += forward.WithZ( 0 ).Normal.Dot( Owner.Velocity.Normal ) * Vector3.Forward * ForwardBobbing * horizontalForwardBob;

		// Apply left bobbing and up/down bobbing
		acceleration += Vector3.Left * WalkCycle( 0.5f, 2f ) * speed * WalkCycleOffsets.y * (1 + sprintLerp) * Time.Delta;
		acceleration += Vector3.Up * WalkCycle( 0.5f, 2f, true ) * speed * WalkCycleOffsets.z * Time.Delta;

		acceleration += left.WithZ( 0 ).Normal.Dot( Owner.Velocity.Normal ) * Vector3.Left * speed * SideWalkOffset * Time.Delta;

		velocity += acceleration * Time.Delta;

		ApplyDamping( ref acceleration, AccelDamping );

		ApplyDamping( ref velocity, Damping );

		acceleration += new Vector3(
			Noise.Perlin( noisePos, 0f, noiseZ ),
			Noise.Perlin( noisePos, 10f, noiseZ ),
			Noise.Perlin( noisePos, 20f, noiseZ )
		) * NoiseScale * Time.Delta;

		velocity = velocity.Normal * Math.Clamp( velocity.Length, 0, VelocityClamp );

		Rotation desiredRotation = Local.Pawn.EyeRot;
		desiredRotation *= Rotation.FromAxis(Vector3.Up, velocity.y * RotationScale);
		desiredRotation *= Rotation.FromAxis(Vector3.Forward, -velocity.y * RotationScale - 10f);
		desiredRotation *= Rotation.FromAxis(Vector3.Right, velocity.z * RotationScale);

		Rotation = desiredRotation;

		Position += forward * (velocity.x * VelocityScale + Offset.x);
		Position += left * (velocity.y * VelocityScale + Offset.y);
		Position += up * (velocity.z * VelocityScale + Offset.z + upDownOffset + avoidance * -10);

		Position += (desiredRotation.Forward - camSetup.Rotation.Forward) * -PivotForce;

		// Apply sprinting / avoidance offsets
		var offsetLerp = MathF.Max(sprintLerp, avoidance);

		Rotation *= Rotation.FromAxis(Vector3.Up, velocity.y * (sprintLerp * 40f) + offsetLerp * 30f);

		Position += forward * avoidance;
		Position += left * (velocity.y * sprintLerp * -50f + offsetLerp * -10f);
		Position += up * (offsetLerp * -0f);
	}

	private float WalkCycle( float speed, float power, bool abs = false )
	{
		var sin = MathF.Sin( walkBob * speed );
		var sign = Math.Sign( sin );

		if ( abs )
		{
			sign = 1;
		}

		return MathF.Pow( sin, power ) * sign;
	}

	public void ApplyImpulse( Vector3 impulse )
	{
		acceleration += impulse;
	}

	private void ApplyDamping( ref float value, float damping )
	{
		var magnitude = value;

		var drop = magnitude * damping * Time.Delta;
		value *= (magnitude - drop) / magnitude;
	}

	private void LerpTowards( ref float value, float desired, float speed )
	{
		var delta = (desired - value) * speed * Time.Delta;
		var deltaAbs = MathF.Min( MathF.Abs( delta ), MathF.Abs( desired - value ) ) * MathF.Sign( delta );

		if ( MathF.Abs( desired - value ) < 0.001f )
		{
			value = desired;

			return;
		}

		value += deltaAbs;
	}

	private void ApplyDamping( ref Vector3 value, float damping )
	{
		var magnitude = value.Length;

		if ( magnitude != 0 )
		{
			var drop = magnitude * damping * Time.Delta;
			value *= Math.Max( magnitude - drop, 0 ) / magnitude;
		}
	}
}
