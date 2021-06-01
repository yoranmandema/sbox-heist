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
	float LookUpPitchScale => 80f;
	float LookUpSpeedScale => 80f;
	float UpDownDamping => 10f;

	float NoiseSpeed => 0.8f;

	float NoiseScale => 20f;
	


	Vector3 WalkCycleOffsets => new Vector3(40f,-20f,20f);
	Vector3 Offset => new Vector3(2f,10,-5f);
	float VelocityClamp => 3f;

	float noiseZ = 0;
	float noisePos = 0;
	float upDownOffset = 0;

	public HeistViewModel () {
		noiseZ = Rand.Float(-10000,10000);
	}

	public override void PostCameraSetup( ref CameraSetup camSetup )
	{
		base.PostCameraSetup( ref camSetup );

		AddCameraEffects( ref camSetup );
	}

	private void AddCameraEffects( ref CameraSetup camSetup )
	{
		var speed = Owner.Velocity.Length.LerpInverse( 30, 320 );
		var left = camSetup.Rotation.Left;
		var up = camSetup.Rotation.Up;
		var forward = camSetup.Rotation.Forward;

		if ( Owner.GroundEntity != null )
		{
			walkBob += Time.Delta * 30.0f * speed;
		}

		upDownOffset += speed * -LookUpSpeedScale* Time.Delta;
		upDownOffset += camSetup.Rotation.Forward.z * -LookUpPitchScale * Time.Delta;

		ApplyDamping(ref upDownOffset, UpDownDamping);

		noisePos += Time.Delta * NoiseSpeed;

		acceleration += Vector3.Left * -Local.Client.Input.MouseDelta.x * Time.Delta * MouseScale;
		acceleration += Vector3.Up * -Local.Client.Input.MouseDelta.y * Time.Delta * MouseScale;
		acceleration += -velocity * ReturnForce * Time.Delta;

		acceleration += Vector3.Forward * WalkCycle(0.5f, 5f) * speed * WalkCycleOffsets.x * Time.Delta;
		acceleration += Vector3.Left * WalkCycle(0.5f, 1f)* speed * WalkCycleOffsets.y * Time.Delta;
		acceleration += Vector3.Up * WalkCycle(0.5f, 10f) * speed * WalkCycleOffsets.z * Time.Delta;

		velocity += acceleration * Time.Delta;

		ApplyDamping(ref acceleration, AccelDamping );

		ApplyDamping( ref velocity, Damping );

		acceleration += new Vector3 (
			Noise.Perlin( noisePos, 0f, noiseZ ),
			Noise.Perlin( noisePos, 10f, noiseZ ),
			Noise.Perlin( noisePos, 20f, noiseZ )
		) * NoiseScale * Time.Delta;

		velocity = velocity.Normal * Math.Clamp(velocity.Length, 0, VelocityClamp);

		Rotation = Local.Pawn.EyeRot;
		Rotation *= Rotation.FromYaw(velocity.y * RotationScale);
		Rotation *= Rotation.FromRoll(-velocity.y * RotationScale - 10f);
		Rotation *= Rotation.FromPitch(-velocity.z * RotationScale );

		Position += forward * (velocity.x * VelocityScale+ Offset.x);
		Position += left * (velocity.y* VelocityScale + Offset.y);
		Position += up * (velocity.z* VelocityScale + Offset.z + upDownOffset) ;

		Position += (Rotation.Forward - camSetup.Rotation.Forward) * -PivotForce;
	}

	private float WalkCycle (float speed, float power) {
		return 1f - MathF.Pow(MathF.Sin( walkBob * speed ), power);
	}

	public void ApplyImpulse (Vector3 impulse) {
		acceleration += impulse;
	}

	private void ApplyDamping( ref float value, float damping )
	{
		var magnitude = value;

		var drop = magnitude * damping * Time.Delta;
		value *= ( magnitude - drop) / magnitude;
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
