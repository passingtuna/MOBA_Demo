namespace MobaTypes
{
    // 클라 검증 + SP 파라미터 길이 + Login/Regist 결과 코드 공통
    public static class MobaAccountLimits
    {
        public const int MaxUserIdLength = 50;
        public const int MaxPasswordLength = 255;
        public const int MaxNickNameLength = 50;

        /// <summary>LoginRequest: 아이디 또는 비밀번호 불일치.</summary>
        public const int ResultLoginInvalidCredentials = -1;

        /// <summary>LoginRequest: LoginDataTable은 있으나 UserData 행 없음.</summary>
        public const int ResultLoginMissingUserData = -2;

        /// <summary>SP/패킷 검증 실패 (길이·빈 값).</summary>
        public const int ResultInvalidInput = -3;

        /// <summary>DB/서버 오류.</summary>
        public const int ResultServerError = -99;
    }
}
