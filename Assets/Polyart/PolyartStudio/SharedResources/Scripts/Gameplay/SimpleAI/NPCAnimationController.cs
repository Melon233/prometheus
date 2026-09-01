using System.Collections;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;


namespace Polyart
{
    public class NPCAnimationController : MonoBehaviour
    {
        public struct CycleSettings
        {
            public float blendIn;
            public AnimationClip blendInClip;

            public AnimationClip cycleClip;

            public float blendOut;
            public AnimationClip blendOutClip;

            public CycleSettings(float blendIn, AnimationClip blendInClip, AnimationClip cycleClip, float blendOut, AnimationClip blendOutClip)
            {
                this.blendIn = blendIn;
                this.blendInClip = blendInClip;
                this.cycleClip = cycleClip;
                this.blendOut = blendOut;
                this.blendOutClip = blendOutClip;
            }
        }

        public struct OneShotSettings
        {
            public AnimationClip oneShotAnim;
            public float blendTime;

            public OneShotSettings(AnimationClip oneShotAnim, float blendTime)
            {
                this.oneShotAnim = oneShotAnim; 
                this.blendTime = blendTime;
            }
        }

        private bool isInitialized = false;

        private PlayableGraph playableGraph;
        private AnimationMixerPlayable topLevelMixer;
        private AnimationMixerPlayable locomotionMixer;
        private AnimationClipPlayable idle, move;
        private AnimationMixerPlayable cycleMixer;
        private AnimationClipPlayable cycleStart, cycleLoop, cycleEnd;
        private AnimationMixerPlayable oneShotMixer;
        private AnimationClipPlayable oneShot;
        private PlayableOutput playableOutput;

        public CycleSettings cycleSettings;
        public OneShotSettings oneShotSettings;

        private void Start()
        {
            Initialize();
        }

        public void Initialize()
        {
            if (isInitialized) return;
            isInitialized = true;

            if (playableGraph.IsValid())
                return;

            playableGraph = PlayableGraph.Create();
            playableGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

            playableOutput = AnimationPlayableOutput.Create(playableGraph, "Animation", GetComponent<Animator>());

            topLevelMixer = AnimationMixerPlayable.Create(playableGraph, 2);
            playableOutput.SetSourcePlayable(topLevelMixer);

            locomotionMixer = AnimationMixerPlayable.Create(playableGraph, 2);
            topLevelMixer.ConnectInput(0, locomotionMixer, 0);
            playableGraph.GetRootPlayable(0).SetInputWeight(0, 1f);

            playableGraph.Connect(idle, 0, locomotionMixer, 0);
            playableGraph.Connect(move, 0, locomotionMixer, 1);

            // Plays the Graph.
            playableGraph.Play();
        }

        private void Update()
        {
            playableGraph.Evaluate(Time.deltaTime);
        }

        public void SetMoveAnim(AnimationClip anim)
        {
            SetAnimOnPlayable(anim, ref move, ref locomotionMixer, 1);
        }

        public void SetIdleAnim(AnimationClip anim)
        {
            SetAnimOnPlayable(anim, ref idle, ref locomotionMixer, 0);
        }

        public void SetCycleAnims(AnimationClip start, AnimationClip loop, AnimationClip end)
        {
            Debug.Log($"Locomotion = {topLevelMixer.GetInputWeight(0)}, Cycle = {topLevelMixer.GetInputWeight(1)}");
            SetAnimOnPlayable(start, ref cycleStart, ref cycleMixer, 0);
            SetAnimOnPlayable(loop, ref cycleLoop, ref cycleMixer, 1);
            SetAnimOnPlayable(end, ref cycleEnd, ref cycleMixer, 2);
        }

        private void SetAnimOnPlayable(AnimationClip anim, ref AnimationClipPlayable playable, ref AnimationMixerPlayable mixer, int position)
        {
            if (!playableGraph.IsValid())
            {
                Debug.Log("Animation Player is NOT Valid.");
                return;
            }

            if (playable.IsValid())
            { 
                if (anim == playable.GetAnimationClip())
                {
                    return;
                }
                else
                {
                    mixer.DisconnectInput(position);
                    playable.Destroy();
                }
            }

            playable = AnimationClipPlayable.Create(playableGraph, anim);
            playable.GetAnimationClip().wrapMode = WrapMode.Loop;
            mixer.ConnectInput(position, playable, 0);
        }

        public void UpdateWalkSpeed(float velocity, float maxSpeed, bool instantBlend)
        {
            if (instantBlend)
            {
                locomotionMixer.SetInputWeight(0, 0f);
                locomotionMixer.SetInputWeight(1, 1f);

                return;
            }
            float weight = Mathf.InverseLerp(0f, maxSpeed, velocity);
            locomotionMixer.SetInputWeight(0, Mathf.Clamp(1f - weight, 0f, 1f) );
            locomotionMixer.SetInputWeight(1, Mathf.Clamp(weight, 0f, 1f) );
        }

        public void SetIdle()
        {
            if (!playableGraph.IsValid())
                return;

            locomotionMixer.SetInputWeight(0, 1f);
            locomotionMixer.SetInputWeight(1, 0f);

            topLevelMixer.SetInputWeight(0, 1f);
            topLevelMixer.SetInputWeight(1, 0f); 
        }

        public void StartCycle(CycleSettings cycleSettings)
        {
            this.cycleSettings = cycleSettings;

            DisconnectCycle();
            DisconnectOneShot();

            cycleMixer = AnimationMixerPlayable.Create(playableGraph, 3);
            topLevelMixer.ConnectInput(1, cycleMixer, 0);

            SetAnimOnPlayable(cycleSettings.blendInClip, ref cycleStart, ref cycleMixer, 0);
            SetAnimOnPlayable(cycleSettings.cycleClip, ref cycleLoop, ref cycleMixer, 1);
            SetAnimOnPlayable(cycleSettings.blendOutClip, ref cycleEnd, ref cycleMixer, 2);

            cycleStart.SetTime(0);
            cycleStart.SetSpeed(1);
            cycleStart.Play();
            cycleLoop.Play();

            cycleMixer.SetInputWeight(0, 1f);
            cycleMixer.SetInputWeight(1, 0f);
            cycleMixer.SetInputWeight(2, 0f);

            StartCoroutine(CycleBlendIn());
        }

        public void StartOneShot(OneShotSettings oneshotSettings)
        {
            this.oneShotSettings = oneshotSettings;

            DisconnectOneShot();
            DisconnectCycle();

            oneShotMixer = AnimationMixerPlayable.Create(playableGraph, 1);
            topLevelMixer.ConnectInput(1, oneShotMixer, 0);

            SetAnimOnPlayable(oneShotSettings.oneShotAnim, ref oneShot, ref oneShotMixer, 0);

            oneShotMixer.SetInputWeight(0, 1f);

            StartCoroutine(OneShotBlendIn());
        }

        private IEnumerator OneShotBlendIn()
        {
            float blendDuration = oneShotSettings.blendTime;
            float elapsed = 0f;

            while (elapsed < blendDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / blendDuration;
                topLevelMixer.SetInputWeight(0, Mathf.Clamp(1f - t, 0f, 1f));
                topLevelMixer.SetInputWeight(1, Mathf.Clamp(t, 0f, 1f));
                yield return null;
            }

            yield return new WaitForSeconds(oneShot.GetAnimationClip().length - (2 * blendDuration));

            elapsed = 0f;
            while (elapsed < blendDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / blendDuration;
                topLevelMixer.SetInputWeight(1, Mathf.Clamp(1f - t, 0f, 1f));
                topLevelMixer.SetInputWeight(0, Mathf.Clamp(t, 0f, 1f));
                yield return null;
            }

            topLevelMixer.SetInputWeight(0, 1f);
            topLevelMixer.SetInputWeight(1, 0f);

            DisconnectOneShot();
        }

        private IEnumerator CycleBlendIn()
        {
            float blendDuration = cycleSettings.blendIn;
            float elapsed = 0f;

            while (elapsed < blendDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / blendDuration;
                topLevelMixer.SetInputWeight(0, Mathf.Clamp(1f - t, 0f, 1f) );
                topLevelMixer.SetInputWeight(1, Mathf.Clamp(t, 0f, 1f) );
                yield return null;
            }

            cycleMixer.SetInputWeight(0, 1f);
            cycleMixer.SetInputWeight(1, 0f);

            float blendTime = cycleSettings.blendOut; // Blend before animation ends
            yield return new WaitForSeconds(cycleStart.GetAnimationClip().length - blendTime - blendDuration);

            elapsed = 0f;
            while (elapsed < blendTime)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / blendTime;
                cycleMixer.SetInputWeight(0, Mathf.Clamp(1f - t, 0f, 1f) );
                cycleMixer.SetInputWeight(1, Mathf.Clamp(t, 0f, 1f) );
                yield return null;
            }

            cycleMixer.SetInputWeight(0, 0f);
            cycleMixer.SetInputWeight(1, 1f);
        }

        public void EndCycle()
        {
            StartCoroutine(BlendOutCycle());
            float endTime = cycleEnd.GetAnimationClip().length;
            Invoke("DisconnectCycle", endTime);
        }

        private void DisconnectCycle()
        {
            locomotionMixer.SetInputWeight(0, 1f);
            locomotionMixer.SetInputWeight(1, 0f);

            if (cycleMixer.IsValid())
            {
                topLevelMixer.DisconnectInput(1);
                cycleMixer.Destroy();
            }
            if (cycleStart.IsValid())
            {
                cycleStart.Destroy();
            }
            if (cycleLoop.IsValid())
            {
                cycleLoop.Destroy();
            }
            if (cycleEnd.IsValid())
            {
                cycleEnd.Destroy();
            }
        }

        private void DisconnectOneShot()
        {
            locomotionMixer.SetInputWeight(0, 1f);
            locomotionMixer.SetInputWeight(1, 0f);

            if (oneShotMixer.IsValid())
            {
                topLevelMixer.DisconnectInput(1);
            }
            if (oneShot.IsValid())
            {
                oneShot.Destroy();
            }
        }

        private IEnumerator BlendOutCycle()
        {
            float blendTime = cycleSettings.blendIn;
            float elapsed = 0f;

            cycleEnd.SetTime(0);
            cycleEnd.SetSpeed(1);

            while (elapsed < blendTime)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / blendTime;
                cycleMixer.SetInputWeight(1, Mathf.Clamp(1f - t, 0f, 1f) );
                cycleMixer.SetInputWeight(2, Mathf.Clamp(t, 0f, 1f) );                
                yield return null;
            }

            cycleMixer.SetInputWeight(1, 0f);
            cycleMixer.SetInputWeight(2, 1f); 

            float returnBlendDuration = cycleSettings.blendOut;

            yield return new WaitForSeconds(cycleEnd.GetAnimationClip().length - blendTime - returnBlendDuration);

            elapsed = 0f;
            while (elapsed < returnBlendDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / returnBlendDuration;
                topLevelMixer.SetInputWeight(1, Mathf.Clamp(1f - t, 0f, 1f) );
                topLevelMixer.SetInputWeight(0, Mathf.Clamp(t, 0f, 1f) );
                yield return null;
            }

            topLevelMixer.SetInputWeight(0, 1f);
            topLevelMixer.SetInputWeight(1, 0f);
        }

        public void DisableAnimController()
        {
            if (playableGraph.IsValid())
            {
                // Destroys all Playables and PlayableOutputs created by the graph.
                playableGraph.Destroy();
            }
        }

        void OnDisable()
        {
            DisableAnimController();
        }

        private void OnDestroy()
        {
            DisableAnimController();
        }

        private void OnApplicationQuit()
        {
            DisableAnimController();
        }
    }


}