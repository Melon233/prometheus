using System;
using System.Collections.Generic;
using Spine;
using Spine.Unity;
using UnityEngine;
using static Spine.AnimationState;
using AnimationState = Spine.AnimationState;

namespace Xuan.Prometheus.Component
{
    public enum AnimationName
    {
        none,
        idle1_1,
        WALK,
        run,
        jump_atk_start,
        jump_atk_loop,
        jump_atk_end,
        city_jump_loop,
        ON_AIR,
        LAND,
        atk1,
        atk2,
        atk3,
        atk4,
        dodge_front_move,
        dodge_back_move
    }
    public enum FaceDir
    {
        Left,
        Right
    }
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(SkeletonAnimation))]
    [RequireComponent(typeof(VfxComponent))]
    public class SpineComponent : MonoComponent
    {
        AnimationState aniState;

        [NonSerialized] public SkeletonAnimation spineAnimator;
        public AnimationLibrary animationLib;
        public Transform rotateRoot;
        public List<AnimationExecutor> executors;
        public FaceDir CurFaceDir
        {
            get
            {
                if (spineAnimator.skeleton.ScaleX > 0)
                    return FaceDir.Right;
                return FaceDir.Left;
            }

            set
            {
                if (value == FaceDir.Right)
                {
                    spineAnimator.skeleton.ScaleX = 1;
                    rotateRoot.rotation = Quaternion.Euler(Vector3.zero);
                }
                else
                {
                    spineAnimator.skeleton.ScaleX = -1;
                    rotateRoot.rotation = Quaternion.Euler(new Vector3(0f, 180f, 0f));
                }
            }
        }
        public void SetFaceDir(Vector2 moveDir)
        {
            if (moveDir.x > 0)
                CurFaceDir = FaceDir.Right;
            else if (moveDir.x < 0)
                CurFaceDir = FaceDir.Left;
        }
        public void SetFaceDir(float x)
        {
            if (x > 0)
                CurFaceDir = FaceDir.Right;
            else if (x < 0)
                CurFaceDir = FaceDir.Left;
        }

        private void Awake()
        {
            spineAnimator = GetComponent<SkeletonAnimation>();
            animationLib = Instantiate(animationLib);
        }
        void Start()
        {
            aniState = spineAnimator.state;
            animationLib.Init(this, GetComponent<VfxComponent>());
        }

        // public TrackEntry Play(string aniName, bool loop = false, float mixDuration = 0.2f, int track = 0)
        // {
        //     Debug.Log("播放动画" + aniName);
        //     var trackEntry = spineAnimator.AnimationState.SetAnimation(track, aniName, loop);
        //     trackEntry.MixDuration = mixDuration;
        //     return trackEntry;
        // }
        // public TrackEntry Play(AnimationReferenceAsset animation,
        //                        bool loop = false,
        //                        int track = 0,
        //                        float mixDuration = 0.2f,
        //                        bool canRefresh = false,
        //                        AnimationReferenceAsset nextAni = null,
        //                        bool nextLoop = false,
        //                        TrackEntryEventDelegate onEvent = null)
        // {
        //     var curTrack = aniState.GetCurrent(track);
        //     if (!canRefresh && animation.Animation == curTrack?.Animation) return curTrack;
        //     var entry = aniState.SetAnimation(track, animation, loop);
        //     // Debug.Log("播放动画" + animation.name);
        //     entry.MixDuration = mixDuration;

        //     if (nextAni)
        //     {
        //         var nextEntry = aniState.AddAnimation(track, nextAni, nextLoop, 0f);
        //         if (onEvent != null) { nextEntry.Event += onEvent; }
        //         return nextEntry;
        //     }
        //     else if (onEvent != null) { entry.Event += onEvent; }
        //     if (track != 0) aniState.AddEmptyAnimation(track, 0.2f, 0f);
        //     return entry;
        // }



        public TrackEntry Play(AnimationReferenceAsset animation, bool loop = false, int track = 0, float mixDuration = 0.2f)
        {
            return spineAnimator.AnimationState.SetAnimation(track, animation, loop);
        }
        public TrackEntry Add(AnimationReferenceAsset animation, bool loop = false, int track = 0, float mixDuration = 0.2f)
        {
            return spineAnimator.AnimationState.AddAnimation(track, animation, loop, 0f);
        }
        public void Stop(int track = 0, float mixDuration = 0.2f)
        {
            if (mixDuration == 0f)
                spineAnimator.AnimationState.ClearTrack(track);
            else
                spineAnimator.AnimationState.SetEmptyAnimation(track, mixDuration);
        }
        public void SetSpeed(int track = 0, float speed = 1f)
        {
            spineAnimator.AnimationState.GetCurrent(track).TimeScale = speed;
        }
        public string GetCurrentAnimation(int track = 0)
        {
            return spineAnimator.AnimationState.GetCurrent(track)?.Animation?.Name;
        }

        public bool IsPlaying(AnimationName animationName, int track = 0)
        {
            return spineAnimator.AnimationState.GetCurrent(track)?.Animation?.Name == animationName.ToString();
        }
    }

    public static class SpineExtensions
    {
        public static float NormalizedTime(this TrackEntry trackEntry)
        {
            return trackEntry.AnimationTime / trackEntry.Animation.Duration;
        }
    }
}