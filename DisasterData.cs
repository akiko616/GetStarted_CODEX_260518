using System;
using System.Collections.Generic;
using UnityEngine; // 필요하다면 추가

namespace TRAINEE
{
    /// <summary>
    /// 이 클래스는 Python 툴에 의해 자동 생성되었습니다.
    /// 수동으로 수정하지 마세요.
    /// </summary>
    [Serializable]
    public class DisasterData : DataBase
    {
        public EScenario scenario;
        public string roomid;
        public string disasterid;
        public ERoomElementType roomElementType;
        public EDisasterState disasterState;
    }
}