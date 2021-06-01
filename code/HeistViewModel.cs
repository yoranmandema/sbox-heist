using Sandbox;
using System;
using System.Linq;

partial class HeistViewModel : BaseViewModel
{
	float walkBob = 0;
	Vector3 velocity;
	Vector3 force; 
	Vector3 acceleration;
	float mouseScale = 1.5f;
	float returnForce = 400f;
	float damping = 20f;
	float accelDamping = 0.2f;
	float pivotForce = 1000f;
	float rotationScale = 2.5f;

	Vector3 offset = new Vector3(2f,10,-3f);
	float velocityClamp = 3f;


	public override void PostCameraSetup( ref CameraSetup camSetup )
	{
		base.PostCameraSetup( ref camSetup );

		// camSetup.ViewModelFieldOfView = camSetup.FieldOfView + (FieldOfView - 80);

		AddCameraEffects( ref camSetup );
	}

	private void AddCameraEffects( ref CameraSetup camSetup )
	{
		//
		// Bob up and down based on our walk movement
		//
		var speed = Owner.Velocity.Length.LerpInverse( 0, 320 );
		var left = camSetup.Rotation.Left;
		var up = camSetup.Rotation.Up;
		var forward = camSetup.Rotation.Forward;

		if ( Owner.GroundEntity != null )
		{
			walkBob += Time.Delta * 30.0f * speed;
		}

		acceleration += Vector3.Left * -Local.Client.Input.MouseDelta.x * Time.Delta * mouseScale;
		acceleration += Vector3.Up * -Local.Client.Input.MouseDelta.y * Time.Delta * mouseScale;
		acceleration += -velocity * returnForce * Time.Delta;

		acceleration += Vector3.Forward * WalkCycle(0.5f, 5f) * speed * 0.2f;
		acceleration += Vector3.Left * WalkCycle(0.5f, 1f)* speed * -0.25f;
		acceleration += Vector3.Up * WalkCycle(0.5f, 10f) * speed * 0.25f;


		velocity += acceleration * Time.Delta;

		// velocity *= new Vector3(0,1,0);

		ApplyDamping(ref acceleration, accelDamping );

		ApplyDamping( ref velocity, damping );

		velocity = velocity.Normal * Math.Clamp(velocity.Length, 0, velocityClamp);

		DebugOverlay.ScreenText(new Vector2(100,100), 0, Color.Red, $"{Math.Round(acceleration.x,2)} {Math.Round(acceleration.y,2)} {Math.Round(acceleration.z,2)}");
		DebugOverlay.ScreenText(new Vector2(100,100), 1, Color.Red, $"{velocity.x} {velocity.y} {velocity.z}");

		Rotation = Local.Pawn.EyeRot;
		Rotation *= Rotation.FromYaw(velocity.y * rotationScale);
		Rotation *= Rotation.FromRoll(-velocity.y * rotationScale);
		Rotation *= Rotation.FromPitch(-velocity.z * rotationScale );

		Position += forward * (velocity.x * 10f+ offset.x);
		Position += left * (velocity.y* 10f + offset.y);
		Position += up * (velocity.z* 10f + offset.z);

		Position += (Rotation.Forward - camSetup.Rotation.Forward) * -pivotForce;
	}

	private float WalkCycle (float speed, float power) {
		return 1f - MathF.Pow(MathF.Sin( walkBob * speed ), power);
	}

	public void ApplyImpulse (Vector3 impulse) {
		acceleration += impulse;
	}

	private void ApplyDamping( ref float value, float damping )
	{
		if ( value != 0 )
		{
			var drop = value * damping * Time.Delta;
			value *= Math.Max( value - drop, 0 ) / value;
		}
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
