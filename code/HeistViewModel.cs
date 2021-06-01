using Sandbox;
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
	float LookUpScale => 5f;

	Vector3 WalkCycleOffsets => new Vector3(40f,-20f,20f);
	Vector3 Offset => new Vector3(2f,10,-3f);
	float VelocityClamp => 3f;


	public override void PostCameraSetup( ref CameraSetup camSetup )
	{
		base.PostCameraSetup( ref camSetup );

		AddCameraEffects( ref camSetup );
	}

	private void AddCameraEffects( ref CameraSetup camSetup )
	{
		var speed = Owner.Velocity.Length.LerpInverse( 0, 320 );
		var left = camSetup.Rotation.Left;
		var up = camSetup.Rotation.Up;
		var forward = camSetup.Rotation.Forward;

		if ( Owner.GroundEntity != null )
		{
			walkBob += Time.Delta * 30.0f * speed;
		}

		acceleration += Vector3.Left * -Local.Client.Input.MouseDelta.x * Time.Delta * MouseScale;
		acceleration += Vector3.Up * -Local.Client.Input.MouseDelta.y * Time.Delta * MouseScale;
		acceleration += -velocity * ReturnForce * Time.Delta;

		acceleration += Vector3.Forward * WalkCycle(0.5f, 5f) * speed * WalkCycleOffsets.x * Time.Delta;
		acceleration += Vector3.Left * WalkCycle(0.5f, 1f)* speed * WalkCycleOffsets.y * Time.Delta;
		acceleration += Vector3.Up * WalkCycle(0.5f, 10f) * speed * WalkCycleOffsets.z * Time.Delta;

		velocity += acceleration * Time.Delta;

		ApplyDamping(ref acceleration, AccelDamping );

		ApplyDamping( ref velocity, Damping );

		velocity = velocity.Normal * Math.Clamp(velocity.Length, 0, VelocityClamp);

		Rotation = Local.Pawn.EyeRot;
		Rotation *= Rotation.FromYaw(velocity.y * RotationScale);
		Rotation *= Rotation.FromRoll(-velocity.y * RotationScale);
		Rotation *= Rotation.FromPitch(-velocity.z * RotationScale );

		Position += forward * (velocity.x * VelocityScale+ Offset.x);
		Position += left * (velocity.y* VelocityScale + Offset.y);
		Position += up * (velocity.z* VelocityScale + Offset.z + camSetup.Rotation.Forward.z * -LookUpScale) ;

		Position += (Rotation.Forward - camSetup.Rotation.Forward) * -PivotForce;
	}

	private float WalkCycle (float speed, float power) {
		return 1f - MathF.Pow(MathF.Sin( walkBob * speed ), power);
	}

	public void ApplyImpulse (Vector3 impulse) {
		acceleration += impulse;
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
