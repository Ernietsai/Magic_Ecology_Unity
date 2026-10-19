using UnityEngine;
using UnityEngine.InputSystem;

public class CameraMainController : MonoBehaviour
{
    //星艦物件、可調常數
    public GameObject starship;
    //public GameObject routeFloor;
    public float camera_speed;
    public float camera_reserve;
    public float camera_zoom_speed;
    public int cameraMode;
    private int previousCameraMode;

    //私有計算變數
    private float   max_view_direction;  //最大視角上下公轉角度
    private Vector3 position_relative;   //角色、相機相對位置
    private float   distance_camera;     //角色、相機距離
    private Vector2 mouse_move;          //滑鼠移動量（舊版Input Manager）
    private Vector2 look_input;          //滑鼠移動量（新版Input Actions）
    private Vector2 view_direction;      //相機公轉量（由mouse_move換算）
    private bool    look_movable;        //相機方向是否可移動
    private float   look_zoom;           //相機放大倍率
    private AgentPerception starshipScript;
    private Vector3 targetPos;
    private Vector3 cameraPos;
    private float cameraDirection;

    //private float cameraHeight = 30f;

    //輸入偵測器
    private InputReader input_reader;

    //[SerializeField] private InputAction input_action;

    void Start()
    {
        previousCameraMode = cameraMode;
        if (cameraMode == 1)
        {
            followStart();
        }/*
        else if (cameraMode == 2)
        {
            farStart();
        }*/
    }

    void Update()
    {
        if (cameraMode == 1)
        {
            if (cameraMode != previousCameraMode)
            {
                followStart();
                previousCameraMode = cameraMode;
            }
            followUpdate();
        }
        else if (cameraMode == 2)
        {/*
            if (cameraMode != previousCameraMode)
            {
                farStart();
                previousCameraMode = cameraMode;
            }
            farUpdate();*/
        }
    }
    /*
    void farStart()
    {
        starshipScript = starship.GetComponent<AgentPerception>();
    }
    
    void farUpdate()
    {   if (starshipScript.UpdateCamera)
        {
            targetPos = new Vector3(starshipScript.routeFloor.localPosition.x, starshipScript.routeFloor.localPosition.y + cameraHeight, starshipScript.routeFloor.localPosition.z);
            cameraDirection = starshipScript.targetDirection - Mathf.PI * 0.5f;
            distance_camera = 0.4f * starshipScript.targetDistance * (1f / Mathf.Tan(Camera.main.fieldOfView / 2f * Mathf.PI / 180f));
            cameraPos = starshipScript.routeFloor.localPosition + new Vector3(distance_camera * Mathf.Cos(cameraDirection), cameraHeight, distance_camera * Mathf.Sin(cameraDirection));
            transform.localPosition = cameraPos;
            transform.LookAt(targetPos);
            starshipScript.UpdateCamera = false;
        }
    }
    */

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void followStart()
    {
        /*
        // Disable all action maps initially
        // Assuming 'InputReader' is a reference to your project-wide InputReader asset
        InputReader.Disable();
        // Manually enable the default action map for player input
        InputReader.Player.Enable();
        */

        starshipScript = starship.GetComponent<AgentPerception>();
        position_relative = this.transform.position - starship.transform.TransformPoint(starshipScript.rb.centerOfMass);
        distance_camera = position_relative.magnitude;
        max_view_direction = Mathf.PI / 2;
        view_direction = new Vector2(-max_view_direction, 0);
    }

    // Update is called once per frame
    void followUpdate()
    {
        //Debug.Log(starshipScript != null);
        //Debug.Log(starship.transform.TransformPoint(starshipScript.rb.centerOfMass));
        look_input = input_reader.Player.Look.ReadValue<Vector2>();
        look_movable = input_reader.Player.LookMovable.IsPressed();
        look_zoom = input_reader.Player.LookZoom.ReadValue<float>();
        //if (Input.GetKey(KeyCode.Mouse1))
        if (look_movable)
        {
            //mouse_move = Camera.main.ScreenToViewportPoint(new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")));
            mouse_move = look_input;  // * 0.00001f
            view_direction = new Vector2(view_direction.x - mouse_move.x * camera_speed * Time.deltaTime, view_direction.y - mouse_move.y * camera_speed * Time.deltaTime);
            if (view_direction.y > max_view_direction - camera_reserve)
            {
                view_direction.y = max_view_direction - camera_reserve;
            }
            else if (view_direction.y < -max_view_direction + camera_reserve)
            {
                view_direction.y = -max_view_direction + camera_reserve;
            }
            Cursor.lockState = CursorLockMode.Locked;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
        }
        this.transform.position = starship.transform.TransformPoint(starshipScript.rb.centerOfMass) + new Vector3(distance_camera * Mathf.Cos(view_direction.x) * Mathf.Cos(view_direction.y), distance_camera * Mathf.Sin(view_direction.y), distance_camera * Mathf.Sin(view_direction.x) * Mathf.Cos(view_direction.y));
        //this.transform.position = starship.transform.TransformPoint(starshipScript.rb.centerOfMass) + position_relative + new Vector3(starship.transform.TransformPoint(starshipScript.rb.centerOfMass).x + position_relative.x + distance_camera * Mathf.Cos(view_direction.x) * Mathf.Cos(view_direction.y), starship.transform.TransformPoint(starshipScript.rb.centerOfMass).y + position_relative.y + distance_camera * Mathf.Sin(view_direction.y), starship.transform.TransformPoint(starshipScript.rb.centerOfMass).z + position_relative.z + distance_camera * Mathf.Sin(view_direction.x) * Mathf.Cos(view_direction.y));
        this.transform.LookAt(starship.transform);
        if (view_direction.y == max_view_direction)
        {
            this.transform.rotation = Quaternion.Euler(new Vector3(90, -view_direction.x * 180 / Mathf.PI - 90, 0));
        }
        else if (view_direction.y == -max_view_direction)
        {
            this.transform.rotation = Quaternion.Euler(new Vector3(-90, -view_direction.x * 180 / Mathf.PI - 90, 0));

        }

        //相機倍率調整
        if (look_zoom != 0)
        {
            distance_camera += -camera_zoom_speed * look_zoom;
            if (distance_camera < 0)
            {
                distance_camera = 0;
            }
        }
        //Debug.Log("新版：" + look_input + "，舊版：" + mouse_move*10000);
        //Debug.Log(look_zoom);
    }

    private void Awake()
    {
        input_reader = new InputReader();
    }
    private void OnEnable()
    {
        if (input_reader != null)
            input_reader.Enable();
    }
    private void OnDisable()
    {
        if (input_reader != null)
            input_reader.Disable();
    }
}
