﻿ 
using System;
using System.Text;
using UnityEngine;

using UnityEngine.EventSystems;
using System.Collections.Generic;
using UnityEngine.XR.WSA.Input;

namespace WinMRSnippets.Samples.Input
{
    public class WinMREventData : PointerEventData
    {
        public GameObject current;                 
        public WinMREventData (EventSystem e) : base(e) { }

        public override void Reset()
        {
            current = null;           
            base.Reset();
        }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();

            if (this.pointerCurrentRaycast.isValid)
            {
                builder.AppendFormat("Raycast: {0} ({1});",  pointerCurrentRaycast.gameObject.name,
                                        "0.0" //  pointerCurrentRaycast.distance
                                    );
            }
            else
                builder.Append("Raycast:invalid"); 


            if ( this.pointerEnter != null )
            {
                builder.AppendFormat("PointerEnter:{0};", Helper.GetName(pointerEnter)); 
            }
            if ( this.pointerPress != null )
            {
                builder.AppendFormat("PointerPress:{0};", Helper.GetName(pointerPress));
            }

            if (this.lastPress != null)
            {
                builder.AppendFormat("lastPress:{0};", Helper.GetName(lastPress));
            }

            if (this.IsPointerMoving())  
            {
                builder.Append("moving: true;"); 
            }

            return builder.ToString(); 
        }
    }

    public enum InputSourceKind
    {
         Unsupported, 
         Gaze, 
         MotionController, 
         GamepadController 
    };


    [System.Flags]
    public enum InputOption
    {
        None = 0x000,
        Gaze = 0x001,
        MotionController = 0x010,
        GazeAndMotionController = InputOption.Gaze | InputOption.MotionController,
        GamepadController = 0x100,
        All = InputOption.Gaze | InputOption.MotionController | InputOption.GamepadController
    };

    public enum InputConcurrency
    {
        Single,
        Multiple
    };


    public class InputSource
    {
        public uint ControllerId;
        public InputSourceKind Kind;  
        public Vector3 Position;
        public Vector3 ForwardPointer; 
        public WinMREventData pointerEvent; 
        public GameObject currentPoint;
        public GameObject currentPressed;
        public GameObject currentDragging;

        public bool IsMotionControllerSelectPressed;
        public bool IsMotionControllerSelectReleased;
       
        public bool IsSelectPressed
        {
            get
            {
                return IsMotionControllerSelectPressed || IsGamepadControllerAPressed ;  
            }
        }

        public bool IsSelectRelased
        {
            get
            {
                return IsMotionControllerSelectReleased || IsGamepadControllerAReleased;
            }
        }

#if !INPUT_MODULE_USE_ONLY_SELECT  
        public bool IsMotionControllerMenuPressed;
        public bool IsMotionControllerMenuReleased;
        public bool IsMotionControllerGraspPressed;
        public bool IsMotionControllerGraspReleased;
#endif 

        public bool IsGamepadControllerAPressed;
        public bool IsGamepadControllerAReleased;  
        
    }




    public class WinMRInputModule : BaseInputModule
    {
        public LayerMask layerMask;
        public InputOption AllowedSelectInput = InputOption.MotionController;
        public InputOption AllowedCursorInput = InputOption.GazeAndMotionController;
        public InputConcurrency InputConcurrencyMode = InputConcurrency.Single;
        public float MinimumTimeBetweenClicksAcrossAll = 0.3f;
        public float MinimumTimeBetweenClicksSameControl = 0.6f;
        public float TimeToGazeClick = 2.0f;
       

        public static WinMRInputModule Instance { get { return instance; } }


        #region class members 
        const uint GAZEID = uint.MaxValue;
        const uint GAMEPADID = GAZEID - 1;
        private uint activeInputId = GAZEID;
        private Dictionary<uint, InputSource> _inputSources = new Dictionary<uint, InputSource>();

        private static WinMRInputModule instance = null;
        private GameObject currentLookAtHandler;
        private float currentLookAtHandlerClickTime;

        #endregion 

        [SerializeField]
        private bool forceActive;


        #region MotionController                
        void ToggleControllerListeners(bool stopListening = false)
        {
            if (!stopListening)
            {
                if ( AllowMotionControllerForPosition || AllowMotionControllerForSelect )
                {
                    InteractionManager.InteractionSourceDetected += InteractionManager_InteractionSourceDetected;
                    InteractionManager.InteractionSourceLost += InteractionManager_InteractionSourceLost;
                    if ( AllowMotionControllerForPosition )
                    {
                        InteractionManager.InteractionSourceUpdated += InteractionManager_InteractionSourceUpdated;
                    }
                    if ( AllowMotionControllerForSelect )
                    {
                        InteractionManager.InteractionSourcePressed += InteractionManager_InteractionSourcePressed;
                        InteractionManager.InteractionSourceReleased += InteractionManager_InteractionSourceReleased;
                    }
                }                 
            }
            else
            {
                InteractionManager.InteractionSourceDetected -= InteractionManager_InteractionSourceDetected;
                InteractionManager.InteractionSourcePressed -= InteractionManager_InteractionSourcePressed;
                InteractionManager.InteractionSourceUpdated -= InteractionManager_InteractionSourceUpdated;
                InteractionManager.InteractionSourceReleased -= InteractionManager_InteractionSourceReleased;
                InteractionManager.InteractionSourceLost -= InteractionManager_InteractionSourceLost;
            }
        }


        private void InteractionManager_InteractionSourceDetected(InteractionSourceDetectedEventArgs args)
        {
            if (args.state.source.kind == InteractionSourceKind.Controller)
            {
                AddInputSource(args.state.source.id, InputSourceKind.MotionController);
            }
#if DEBUG 
            else
                Debug.Log("Ignoring source: " + args.state.source.kind);
#endif
        }

        private void InteractionManager_InteractionSourceLost(InteractionSourceLostEventArgs args)
        {
            if (args.state.source.kind == InteractionSourceKind.Controller)
            {
                RemoveInputSource(args.state.source.id);
            }
        }

        private void InteractionManager_InteractionSourcePressed(InteractionSourcePressedEventArgs args)
        {
            InputSource data = null;
            if (AllowMotionControllerForPosition )
            {
                data = GetInputSource(args.state, true);
            }
            else if (AllowGazeForPosition)
            {
                data = GetGazeSource();
            }

            /// For Select events, we 
            /// 1) Always handle the pressed...because this event never fires if Select with Motion Controller is not allowed 
            /// 2) Set ActiveSource to controller - only if AllowMotionControllerForPosition is true  
            /// 3) Only update position if AllowMotionControllerForPosition is true  
            if (data != null )
            {
                data.IsMotionControllerSelectPressed = args.pressType == InteractionSourcePressType.Select;
#if !INPUT_MODULE_USE_ONLY_SELECT
                data.IsMotionControllerGraspPressed = args.pressType == InteractionSourcePressType.Grasp;
                data.IsMotionControllerMenuPressed = args.pressType == InteractionSourcePressType.Menu;
#endif
                if (AllowMotionControllerForPosition &&
                        (args.pressType == InteractionSourcePressType.Select
#if !INPUT_MODULE_USE_ONLY_SELECT
                            || args.pressType == InteractionSourcePressType.Grasp
                            || args.pressType == InteractionSourcePressType.Menu
#endif
                            ))
                {
                    SetActiveInputSource(args.state.source.id, data.Kind);
                    UpdateControllerPosition(data, args.state);
                }
                Debug.Log("Source Pressed: " + args.pressType);
            }
        }


        private void InteractionManager_InteractionSourceReleased(InteractionSourceReleasedEventArgs args)
        {
            InputSource data = null;
            if ( AllowMotionControllerForPosition)
            {
                data = GetInputSource(args.state);
            }
            else if (AllowGazeForPosition)
            {
                data = GetGazeSource();
            }

            if (data != null)
            {
                data.IsMotionControllerSelectReleased = args.pressType == InteractionSourcePressType.Select;
#if !INPUT_MODULE_USE_ONLY_SELECT
                data.IsMotionControllerGraspReleased = args.pressType == InteractionSourcePressType.Grasp;
                data.IsMotionControllerMenuReleased = args.pressType == InteractionSourcePressType.Menu;
#endif
                /// For Release events, we 
                /// 1) Always handle the event...because this event never fires if Select with Motion Controller is not allowed 
                /// 2) Set ActiveSource to controller - only if UseMotionControllerForPosition is true  
                /// 3) Only update position if UseMotionControllerForPosition is true  
                /// 
                if (AllowMotionControllerForPosition &&
                     (
                        args.pressType == InteractionSourcePressType.Select
#if !INPUT_MODULE_USE_ONLY_SELECT
                        || args.pressType == InteractionSourcePressType.Grasp
                        || args.pressType == InteractionSourcePressType.Menu
#endif
                     ))
                {
                    activeInputId = args.state.source.id;
                    UpdateControllerPosition(data, args.state);
                }

                Debug.Log("Source Released: " + args.pressType);
            }

        }

        private void SetActiveInputSource(uint id, InputSourceKind kind)
        {
            if (activeInputId != id)
            {
                activeInputId = id;
                Debug.Log(string.Format ( "**CHANGING INPUT SOURCE to {0} ({1}", id,  kind)) ;
                InputSource source = null; 
                if ( _inputSources.TryGetValue(id, out source  ))
                {
                    if (source.pointerEvent != null )
                    {
                         
                        source.pointerEvent.Reset();  
                    }
                }
            }
        }

        private bool IsActiveInputSource(uint id)
        {
            return activeInputId == id;
        }

        private void InteractionManager_InteractionSourceUpdated(InteractionSourceUpdatedEventArgs args)
        {
            //   Debug.Log("InputModule::SourceUpdated"); 
            InputSource data;
            if (_inputSources.TryGetValue(args.state.source.id, out data))
            {
                UpdateControllerPosition(data, args.state);
            }
        }



        #endregion


        #region Gaze 

        void ProcessGazePointer(WinMREventData pointerEvent , out bool fired )
        {
            fired = false; 
            if (pointerEvent.pointerEnter != null)
            {
                // if the ui receiver has changed, reset the gaze delay timer
                GameObject handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(pointerEvent.pointerEnter);
                if (currentLookAtHandler != handler)
                {
                    currentLookAtHandler = handler;
                    currentLookAtHandlerClickTime = Time.unscaledTime + TimeToGazeClick;
                }

                // if we have a handler and it's time to click, do it now
                if (currentLookAtHandler != null && Time.unscaledTime > currentLookAtHandlerClickTime)
                {
                    ExecuteEvents.ExecuteHierarchy(currentLookAtHandler, pointerEvent, ExecuteEvents.pointerClickHandler);
                    currentLookAtHandlerClickTime = float.MaxValue;
                    fired = true;
                    pointerEvent.clickTime = Time.unscaledTime ; 
                }
            }
            else
            {
                currentLookAtHandler = null;
            }
        }


        #endregion



        #region class helpers 
        InputSourceKind GetInputModuleKind(InteractionSourceKind kind, uint id = 0)
        {
            switch (kind)
            {
                case InteractionSourceKind.Controller:
                    return InputSourceKind.MotionController;
                    break;

                case InteractionSourceKind.Other:
                    if (id == GAZEID)
                        return InputSourceKind.Gaze;
                    else if (id == GAMEPADID)
                        return InputSourceKind.GamepadController;
                    break;

                case InteractionSourceKind.Hand:
                default:
                    return InputSourceKind.Unsupported;
            }
            return InputSourceKind.Unsupported;
        }

        private InputSource GetInputSource(InteractionSourceState state, bool createIfNeeded = false)
        {
            InputSource data = null;
            if (_inputSources.TryGetValue(state.source.id, out data))
            {
                return data;
            }
            else if (createIfNeeded)
            {
                data = AddInputSource(state.source.id, GetInputModuleKind(state.source.kind, state.source.id));
            }
            return data;
        }

        void UpdateControllerPosition ( InputSource data ,   InteractionSourceState state )
        {
            Vector3 position, forward; 
            bool hasPosition = state.sourcePose.TryGetPosition(out position, InteractionSourceNode.Pointer);
            if (hasPosition)
            {
                data.Position = position;
            }

            bool hasForward = state.sourcePose.TryGetForward(out forward, InteractionSourceNode.Pointer);
            if (hasForward)
            {
                data.ForwardPointer = forward;
            }

            Debug.Assert(hasPosition && hasForward, "Expected position and forwared"); 
        }

        private bool vrPresent
        {
            get
            {
#if UNITY_2017_2_OR_NEWER
                return UnityEngine.XR.XRDevice.isPresent;
#else
                return UnityEngine.VR.VRDevice.isPresent;
#endif
            }
        }


        bool IsActiveMotionController(uint motionControllerId)
        {
            return activeInputId == motionControllerId;
        }


        #endregion




        #region InputModule 
        protected override void Awake()
        {
            Debug.Log("InputModule::Awake()"); 
            base.Awake();
            if (instance != null)
            {
                Debug.LogWarning("Trying to instantiate multiple Input Modules is not allowed.");
                DestroyImmediate(this.gameObject);
            }
            instance = this;
            
        }


        public override void ActivateModule()
        {
            Debug.Log("InputModule::ActivateModule()"); 
            base.ActivateModule();
            ToggleControllerListeners(); 
        }

    

        public bool ForceModuleActive
        {
            get { return forceActive; }
            set { forceActive = value; }
        }

        public override bool IsModuleSupported()
        {            
            bool retVal =  vrPresent || forceActive;         
            return retVal;  
        }

        public override bool ShouldActivateModule()
        {
            bool retVal = false;
            if (base.ShouldActivateModule())
            {
                
                return IsModuleSupported(); 
            }       
            return retVal ;
        }


        protected override void Start()
        {
            Debug.Log("InputModule:Start();");
            base.Start();            
            if (ShouldActivateModule())
            {
                var sim = GetComponent<StandaloneInputModule>();
                if (sim != null)
                {                    
                    try
                    {
                        //TODO: not checking if enabled causes exception in Deactivate as eventSystem is null                      
                        var es = sim.GetComponent<EventSystem>(); 
                        if ( es != null )
                            sim.DeactivateModule();
                    }
                    catch ( System.Exception ex )
                    {
                        Debug.Log(ex.Message); 
                    }
                    sim.enabled = false;                     
                }
            }
        }

        public bool TryGetCursorCoordinates ( out Vector3 position , out GameObject target )  
        {
            position = Vector3.zero;
            target = null; 
            InputSource source = null; 
            if ( _inputSources.TryGetValue( activeInputId, out source))
            {                
                if ( source.pointerEvent!= null  && source.pointerEvent.pointerCurrentRaycast.isValid )
                {
                    position = source.pointerEvent.pointerCurrentRaycast.worldPosition;
                    target = source.pointerEvent.pointerCurrentRaycast.gameObject; 
                    return true; 
                }
                else  
                {
                    if (source.Kind == InputSourceKind.Gaze)
                    {
                        position = Camera.main.transform.position + (Camera.main.transform.forward * 1.5f);                      
                        return true;
                    }
                    else if (source.Kind == InputSourceKind.MotionController)
                    {
                        position = source.Position + source.ForwardPointer * 1.5f;                         
                        return true;
                    } 
                }
                 
            }
           
            return false; 
        }

        

        void ProcessRaycast (InputSource data )
        {
            eventSystem.RaycastAll(data.pointerEvent, m_RaycastResultCache);
            data.pointerEvent.pointerCurrentRaycast = FindFirstRaycast(m_RaycastResultCache);
            m_RaycastResultCache.Clear();
        }

       
        public override void DeactivateModule()
        {
            Debug.Log("InputModule::DeactivateModule()");
            base.DeactivateModule();
            ClearSelection();
            ToggleControllerListeners();
        }


        void ClearSelection ()
        {
            var baseEventData = GetBaseEventData();

            foreach (var data in _inputSources.Values)
            {
                // clear all selection
                if (data.pointerEvent != null)
                {
                    HandlePointerExitAndEnter(data.pointerEvent, null);
                    data.pointerEvent.Reset(); 
                } 
            }
            
            eventSystem.SetSelectedGameObject(null, baseEventData);

        }


        protected void DeselectIfSelectionChanged(GameObject currentOverGo, BaseEventData pointerEvent)
        {
             
            var selectHandlerGO = ExecuteEvents.GetEventHandler<ISelectHandler>(currentOverGo);
            // if we have clicked something new, deselect the old thing
            // leave 'selection handling' up to the press event though.
            
            // Selection tracking
            if (selectHandlerGO != eventSystem.currentSelectedGameObject)
            {
                Debug.Log(string.Format("Changing selection. Current gameobject == is {2}. Going to {0}, from {1}",
                    (selectHandlerGO != null) ? selectHandlerGO.name : "null",
                    (eventSystem.currentSelectedGameObject != null) ? eventSystem.currentSelectedGameObject.name : "null", 
                    (currentOverGo != null) ? currentOverGo.name : "null"  
                    ));
                eventSystem.SetSelectedGameObject(null, pointerEvent);
            }
#if DEBUG 
            //else 
            //Debug.Log(string.Format("Sel process {0}, {1}, {2}", currentOverGo.name,
            //                 (pointerEvent.selectedObject != null) ? pointerEvent.selectedObject.name : "null",
            //                 (selectHandlerGO != null) ? selectHandlerGO.name : "null"
            //         ) );
#endif 

        }



        void ProcessGamepad()
        {

        }

        #region InputModule State 

        //Note the naming convention: 
        // Allow*  is used for properties/state that are always on, regardless of current input source 
        // Use* is used for properties/state that is specific to state and current input source 


        bool UseGazeForPosition
        {
            get
            {
                return (AllowGazeForPosition &&  activeInputId == GAZEID);
            }
        }

        bool UseGazeForSelect
        {
            get
            {
                return ((AllowedSelectInput & InputOption.Gaze) != InputOption.None) && (activeInputId == GAZEID);
            }
        }


        bool UseMotionControllerForPosition
        {
            get
            {
                return ( AllowMotionControllerForPosition &&  activeInputId != GAZEID );
            }
        }

        

        bool AllowMotionControllerForPosition
        {
            get
            {
                return ((AllowedCursorInput & InputOption.MotionController) != InputOption.None); 
            }
        }


        bool AllowGazeForPosition
        {
            get
            {
                return ((AllowedCursorInput & InputOption.Gaze) != InputOption.None);
            }
        }

        bool AllowMotionControllerForSelect
        {
            get
            {
                return ((AllowedSelectInput & InputOption.MotionController) != InputOption.None); 
            }
        }

        bool AllowGamepadControllerForSelect
        {
            get
            {
                return ((AllowedSelectInput & InputOption.GamepadController) != InputOption.None);
            }
        }

        private InputSource  GetActiveMotionController()
        {
            InputSource activeSource = null; 
            foreach (var source in _inputSources.Values )
            {
                if (IsActiveMotionController( source.ControllerId))
                {
                    activeSource = source ;
                    break;
                }
            }
            return activeSource; 
        } 

        private InputSource  GetGazeSource ()
        {
            InputSource source; 
            if ( !_inputSources.TryGetValue( GAZEID, out source ))
            {
                source = AddInputSource(GAZEID, InputSourceKind.Gaze);                  
            }
            return source; 
        }

        #endregion 

        public override void Process()
        {

#if DEBUG && FALSE
            if ( !eventSystem.isFocused && ShouldIgnoreEventsOnNoFocus ())
            {
                return; 
            }

           //TODO: Implement navigation Events. 
           if ( eventSystem.sendNavigationEvents )
            {
                string NavigationEventsNotSupported = "Navigation Events are not implemented"; 
                Debug.Assert(false, NavigationEventsNotSupported );
            
            }
#endif

            InputSource activeSource = null;

            if ( UseMotionControllerForPosition )
            {
                activeSource = GetActiveMotionController();
            } 
            else
            {
                Debug.Assert(UseGazeForPosition, "Expcted UseGazeForPosition to be default/fallback" );
                activeSource = GetGazeSource();
            } 
                

            if (activeSource != null)
            {
                if (activeSource.pointerEvent == null)
                {
                    activeSource.pointerEvent = new WinMREventData(eventSystem);
                }
                else
                {
                    activeSource.pointerEvent.Reset();
                }
                activeSource.pointerEvent.delta = Vector2.zero;
                // TODO: is position needed? if so do we translate to Screen coordinates? 

               

                if (UseMotionControllerForPosition)
                {
                    //TODO: Experimental.. 
                    //Vector3 viewportPoint = Camera.main.WorldToViewportPoint(data.Position); 
                    //data.pointerEvent.position = new Vector2(UnityEngine.XR.XRSettings.eyeTextureWidth *viewportPoint.x , UnityEngine.XR.XRSettings.eyeTextureHeight *viewportPoint.y );
                  //  TraceHelper.LogDiff(data.pointerEvent.position.ToString(), TraceCacheGrouping.LastPosition);
                    //End Experimental 
                    activeSource.pointerEvent.pointerCurrentRaycast = new RaycastResult()
                    {
                        worldPosition = activeSource.Position,
                        worldNormal = activeSource.ForwardPointer                         
                    };
                } 
                else if ( UseGazeForPosition )
                {
                    activeSource.pointerEvent.position = new Vector2(UnityEngine.XR.XRSettings.eyeTextureWidth / 2, UnityEngine.XR.XRSettings.eyeTextureHeight / 2);
              //      TraceHelper.LogDiff(data.pointerEvent.position.ToString(), TraceCacheGrouping.LastPosition); 
                    activeSource.pointerEvent.pointerCurrentRaycast = new RaycastResult()
                    {
                        worldPosition = Camera.main.transform.position,
                        worldNormal = Camera.main.transform.forward
                    }; 
                }

                // trigger a raycast
                ProcessRaycast(activeSource);

                //TraceHelper.LogDiff(data.pointerEvent.ToString() + " " + (data.IsMotionControllerSelectPressed ? "down" : "") + (data.IsMotionControllerSelectReleased ? "up" : "")
                //    , TraceCacheGrouping.TrackingState);

                GameObject currentTargetGO = null;
                if (activeSource.pointerEvent != null && activeSource.pointerEvent.pointerCurrentRaycast.isValid)
                    currentTargetGO = activeSource.pointerEvent.pointerCurrentRaycast.gameObject;

                // Handle enter and exit events on the GUI controlls that are hit
                base.HandlePointerExitAndEnter(activeSource.pointerEvent, currentTargetGO);
                 



                bool firedOnGaze; 
                if ( UseGazeForSelect )
                {
                    ProcessGazePointer(activeSource.pointerEvent, out firedOnGaze); 
                }
                else if (activeSource.IsSelectPressed && currentTargetGO != null)
                {
                     DeselectIfSelectionChanged(currentTargetGO, activeSource.pointerEvent);
                    if ((Time.unscaledTime - activeSource.pointerEvent.clickTime) > MinimumTimeBetweenClicksAcrossAll)
                    {
                        activeSource.pointerEvent.current = currentTargetGO;
                        GameObject newPressed = ExecuteEvents.ExecuteHierarchy(currentTargetGO, activeSource.pointerEvent, ExecuteEvents.pointerDownHandler);
                        bool needsToFireClick = true;
                        if (newPressed == null)
                        {
                            // some UI elements might only have click handler and not pointer down handler                            
                            newPressed = ExecuteEvents.ExecuteHierarchy(currentTargetGO, activeSource.pointerEvent, ExecuteEvents.pointerClickHandler);
                            activeSource.pointerEvent.clickTime = Time.unscaledTime;
                            activeSource.pointerEvent.pointerPress = newPressed;
                            needsToFireClick = false; // not needed, optimizer shall remove 
                        }
                        else
                        {
                            activeSource.pointerEvent.pointerPress = newPressed;
                            if (activeSource.pointerEvent.pointerPress == activeSource.pointerEvent.lastPress) // Same button, but maybe different time, two presses
                            {
                                if ((Time.unscaledTime - activeSource.pointerEvent.clickTime) < MinimumTimeBetweenClicksSameControl)
                                {
                                    needsToFireClick = false;
                                    activeSource.pointerEvent.clickCount++;
                                    Debug.Log("Same control debounce");
                                }
                                // no else because we fall back to firing as a new event (via needsToFire == true ) 
                            }

                            if (needsToFireClick)
                            {
                                activeSource.pointerEvent.clickTime = Time.unscaledTime;
                                activeSource.pointerEvent.clickCount = 1;
                                Debug.Log("Firing Click for " + newPressed.name);
                                ExecuteEvents.Execute(newPressed, activeSource.pointerEvent, ExecuteEvents.pointerClickHandler);
                            }
                        }
                    }
                    
#if DEBUG
                    else
                    {
                        //  Debug.Log("Skipped click due to hardware debounce" + currentTargetGO.name); 
                    }
#endif
                }
                else if ( currentTargetGO == null && eventSystem.currentSelectedGameObject)
                {
                    Debug.Log("***Clearing selection***");
                    DeselectIfSelectionChanged(currentTargetGO, activeSource.pointerEvent);
                }

                if (activeSource.IsSelectRelased)
                {
                    if (activeSource.pointerEvent.pointerPress != null) /// data.pointerEvent.pointerPress 
                    {
                        Debug.Log("Pointer Up" + activeSource.pointerEvent.pointerPress.name);
                        ExecuteEvents.Execute(activeSource.pointerEvent.pointerPress, activeSource.pointerEvent, ExecuteEvents.pointerUpHandler);
                        // data.pointerEvent.rawPointerPress = null;
                        //  data.pointerEvent.pointerPress = null;
                    }
                    activeSource.IsMotionControllerSelectReleased = false;

                    
                }

                if (activeSource.IsMotionControllerSelectPressed)
                {
                    activeSource.IsMotionControllerSelectPressed = false;
                }

            }            
        }


        #endregion 

        bool IsSupportedKind ( InputSourceKind kind )
        {
            return kind != InputSourceKind.Unsupported; 
        }


        #region public Methods 

        public InputSource AddInputSource ( uint id , InputSourceKind kind  )
        {
            Debug.Log("InputModule::AddInputSource");
            InputSource source = null;
            if ( IsSupportedKind (kind))
            {
                if (!this._inputSources.TryGetValue(id, out source))
                {
                    source = new InputSource();
                    source.ControllerId = id;
                    source.Kind = kind;
                    this._inputSources.Add(id, source);
                }
            } 
            return source; 

        }

        public void RemoveInputSource ( uint id )
        {
            Debug.Log("InputModule::RemoveController");
            InputSource data; 
            if ( _inputSources.TryGetValue( id, out data )) 
            {               
                this._inputSources.Remove(id);

                // TODO: 
                // When a controller is removed , even if the other controller is still visible, we fall back to gaze, 
                // if there is no other input source, we fallback to gaze too. 
                if (  IsActiveInputSource (id) || 
                   ( this._inputSources.Count <= 1 && this._inputSources.ContainsKey(GAZEID)) )
                {                    
                    SetActiveInputSource(GAZEID, InputSourceKind.Gaze );  
                }                
            }

        }

        public void SetPosition ( uint id,  Vector3 position  )
        {
            InputSource controllerData = null; 
            if ( _inputSources.TryGetValue(id, out controllerData ))
            {
                controllerData.Position = position;  
            }
        }

        public void SetButtonStates ( uint id, bool isTriggerPressed, bool isGrasped , bool isMenuPressed )
        {
            InputSource controllerData = null;
            if (_inputSources.TryGetValue(id, out controllerData))
            {

                ///Since release is stateful, always check released first, 
                controllerData.IsMotionControllerSelectReleased = (controllerData.IsMotionControllerSelectPressed && !isTriggerPressed);
                controllerData.IsMotionControllerSelectPressed = isTriggerPressed;

#if !INPUT_MODULE_USE_ONLY_SELECT 
                controllerData.IsMotionControllerGraspReleased = controllerData.IsMotionControllerGraspPressed && !isGrasped;
                controllerData.IsMotionControllerGraspPressed = isGrasped;

                controllerData.IsMotionControllerMenuReleased = controllerData.IsMotionControllerMenuPressed && !isMenuPressed;
                controllerData.IsMotionControllerMenuPressed = isMenuPressed;                
#endif 

            } 
        }
 

        public void SetForwardPointer (uint id, Vector3 forwardPointer )
        {

            InputSource controllerData = null;
            if (_inputSources.TryGetValue(id, out controllerData))
            {
                controllerData.ForwardPointer = forwardPointer ;
            }
        }

        #endregion 


#if !SKIPTROUBLESHOOTINGCODE
        void DoRandomHitTesting( WinMREventData data )
        {

            Ray ray = new Ray(data.pointerCurrentRaycast.worldPosition, data.pointerCurrentRaycast.worldNormal); 

            Debug.DrawRay(ray.origin, ray.direction, Color.green);
            RaycastHit raycastHit;
            if (Physics.Raycast(ray, out raycastHit, 10f))
            {
                if (raycastHit.collider != null)
                {
                    Debug.Log("hit: " + raycastHit.collider.gameObject.name);
                }
            }

            RaycastHit2D hit2D = Physics2D.Raycast(ray.origin, ray.direction, 10f);
            if (hit2D.collider != null)
            {
                Debug.Log("hit: " + hit2D.collider.gameObject.name);
            }

            var rayCasterList = GameObject.FindObjectsOfType<UnityEngine.UI.GraphicRaycaster>();
            if (rayCasterList != null)
            {
                List<RaycastResult> results = new List<RaycastResult>();
                foreach (UnityEngine.UI.GraphicRaycaster rc in rayCasterList)
                {
                    rc.Raycast(data, results);
                    if (results.Count > 0)
                    {
                        foreach (var result in results)
                        {
                            Debug.Log(string.Format("result - valid ({0}), object({1}), module({2})",
                                result.isValid, (result.gameObject == null) ? "null" : result.gameObject.name, result.module.name));
                        }
                    }
                }
            }
        }

        void ShowButtonStates ( InputSource controllerData  )
        {
            string states = string.Format("{0},{1},{2}",
                   controllerData.IsMotionControllerSelectPressed ? "Trigger Down" : (controllerData.IsMotionControllerSelectReleased ? "Trigger Up" : ""),
                   controllerData.IsMotionControllerGraspPressed ? "Grasp Down" : (controllerData.IsMotionControllerGraspReleased ? "Grasp Up" : ""),
                   controllerData.IsMotionControllerMenuPressed ? "Menu Down" : (controllerData.IsMotionControllerMenuReleased ? "Menu Up" : "")
                   );

            if (states.Length > 5)
                Debug.Log(states);
        }
#endif


#if NOTYET

           private bool ShouldIgnoreEventsOnNoFocus()
        {
            switch (SystemInfo.operatingSystemFamily)
            {
                case OperatingSystemFamily.Windows:
                case OperatingSystemFamily.Linux:
                case OperatingSystemFamily.MacOSX:
#if UNITY_EDITOR
                    if (UnityEditor.EditorApplication.isRemoteConnected)
                        return false;
#endif
                    return true;
                default:
                    Debug.Log("Unexpected Operating System Family"); 
                    return false;
            }
        }
#endif
    }

    internal static class Helper
    {
        public static string GetName (GameObject go)
        {
            return ( go == null ) ? "null" : go.name  ;  
        }

    
    }

}

