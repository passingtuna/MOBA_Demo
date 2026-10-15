USE [Game_DataBase]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

ALTER PROCEDURE [dbo].[LoginRequest]
(
    @UserId     varchar(50),
    @Password   varchar(255),
    @Result     int           OUTPUT,
    @NickName   varchar(50)   OUTPUT,
    @Money      int           OUTPUT,
    @MMR        int           OUTPUT
)
AS
BEGIN
    SET NOCOUNT ON;

    SET @Result   = -1;
    SET @NickName = NULL;
    SET @Money    = 0;
    SET @MMR      = 0;

    DECLARE @uid bigint;

    SELECT @uid = UID
    FROM LoginDataTable
    WHERE UserID = @UserId AND UserPassward = @Password;

    IF (@@ROWCOUNT = 0)
    BEGIN
        SET @Result = -1;
        RETURN;
    END

    SELECT @NickName = NickName,
           @Money    = UserMoney,
           @MMR      = MMR
    FROM UserData
    WHERE UID = @uid;

    IF (@@ROWCOUNT = 0)
    BEGIN
        SET @Result = -2;
        RETURN;
    END

    SET @Result = @uid;

    UPDATE LoginDataTable
    SET LastLoginAt = CURRENT_TIMESTAMP
    WHERE UID = @uid;
END
GO
