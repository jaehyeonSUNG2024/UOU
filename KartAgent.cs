using KartGame.KartSystems;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace KartGame.AI
{
    public class KartAgentVision : Agent, IInput
    {
        [Header("Checkpoints")]
        public Collider[] Colliders;
        public float PassCheckpointReward = 10.0f;

        [Header("Rewards")]
        public float HitPenalty = -1.0f;
        public float TimePenalty = -0.001f;
        public float SpeedReward = 0.001f; // 너무 크면 속도만 땡김

        [Header("Camera")]
        public Vector3 CameraOffset = new Vector3(0, 1.5f, 2.0f);
        public Vector3 CameraRotation = Vector3.zero;
        public int CameraWidth = 84;
        public int CameraHeight = 84;
        public float CameraFOV = 60f;

        ArcadeKart m_Kart;

        bool m_Acceleration;
        bool m_Brake;
        float m_Steering;

        int m_CheckpointIndex;

        Vector3 m_SpawnPos;
        Quaternion m_SpawnRot;
        bool m_SpawnCached;

        Camera m_AgentCamera;
        CameraSensorComponent m_CameraSensor;

        protected override void Awake()
        {
            base.Awake();

            m_Kart = GetComponent<ArcadeKart>();
            if (m_Kart == null)
                Debug.LogError("KartAgentVision: ArcadeKart not found.", this);

            // 런타임에만 카메라 생성 (에디터 모드에서 Inspector 에러 방지)
            if (Application.isPlaying)
            {
                SetupCamera();
            }
        }

        void Start()
        {
            if (!m_SpawnCached)
            {
                m_SpawnPos = transform.position;
                m_SpawnRot = transform.rotation;
                m_SpawnCached = true;
            }
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            // 이미지 관찰은 CameraSensorComponent가 자동 처리
            // 여기서는 Vector 관찰을 아예 쓰지 않음
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            if (m_Kart == null) return;

            InterpretContinuous(actions);

            // 매 스텝 페널티(빨리 완주 유도)
            AddReward(TimePenalty);

            // 속도 보상(선택)
            AddReward(m_Kart.LocalSpeed() * SpeedReward);
        }

        public override void OnEpisodeBegin()
        {
            transform.position = m_SpawnPos;
            transform.rotation = m_SpawnRot;

            m_CheckpointIndex = 0;

            if (m_Kart != null && m_Kart.Rigidbody != null)
            {
                m_Kart.Rigidbody.linearVelocity = Vector3.zero;
                m_Kart.Rigidbody.angularVelocity = Vector3.zero;
            }

            m_Acceleration = false;
            m_Brake = false;
            m_Steering = 0f;
        }

        void InterpretContinuous(ActionBuffers actions)
        {
            // actions.ContinuousActions[0] = steer [-1,1]
            // actions.ContinuousActions[1] = throttle/brake [-1,1]
            m_Steering = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);

            float v = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
            const float dead = 0.1f;

            if (v > dead) { m_Acceleration = true;  m_Brake = false; }
            else if (v < -dead) { m_Acceleration = false; m_Brake = true; }
            else { m_Acceleration = false; m_Brake = false; }
        }

        void OnTriggerEnter(Collider other)
        {
            if (Colliders == null || Colliders.Length == 0) return;

            int expected = (m_CheckpointIndex + 1) % Colliders.Length;

            if (other == Colliders[expected])
            {
                AddReward(PassCheckpointReward);
                m_CheckpointIndex = expected;

                // 마지막 체크포인트면 종료하고 큰 보상 주고 싶으면 여기서 처리 가능
                // if (m_CheckpointIndex == Colliders.Length - 1) { AddReward(2f); EndEpisode(); }
            }
        }

        void OnCollisionEnter(Collision col)
        {
            AddReward(HitPenalty);
            EndEpisode();
        }

        void SetupCamera()
        {
            // 런타임에만 실행
            if (!Application.isPlaying)
                return;

            // 기존 카메라가 있으면 제거
            if (m_AgentCamera != null)
            {
                if (m_AgentCamera.gameObject != null)
                {
                    Destroy(m_AgentCamera.gameObject);
                }
                m_AgentCamera = null;
            }

            // 기존 센서가 있으면 제거
            if (m_CameraSensor != null)
            {
                Destroy(m_CameraSensor);
                m_CameraSensor = null;
            }

            // 카메라 생성
            var camObj = new GameObject("AgentCamera");
            camObj.transform.SetParent(transform);
            camObj.transform.localPosition = CameraOffset;
            camObj.transform.localRotation = Quaternion.Euler(CameraRotation);

            m_AgentCamera = camObj.AddComponent<Camera>();
            m_AgentCamera.fieldOfView = CameraFOV;
            m_AgentCamera.nearClipPlane = 0.1f;
            m_AgentCamera.farClipPlane = 200f;

            // Visual 관찰 센서
            m_CameraSensor = gameObject.AddComponent<CameraSensorComponent>();
            m_CameraSensor.Camera = m_AgentCamera;
            m_CameraSensor.SensorName = "Vision";
            m_CameraSensor.Width = CameraWidth;
            m_CameraSensor.Height = CameraHeight;
            m_CameraSensor.Grayscale = false;

            // 학습용이면 화면 렌더링 끄는 게 보통 빠름(필요 시 true로)
            m_CameraSensor.RuntimeCameraEnable = false;
        }

        public InputData GenerateInput()
        {
            return new InputData
            {
                Accelerate = m_Acceleration,
                Brake = m_Brake,
                TurnInput = m_Steering
            };
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var a = actionsOut.ContinuousActions;
            a[0] = Input.GetAxis("Horizontal");
            a[1] = Input.GetAxis("Vertical");
        }
    }
}
