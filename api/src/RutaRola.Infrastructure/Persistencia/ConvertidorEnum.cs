using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace RutaRola.Infrastructure.Persistencia;

/// <summary>Guarda los enums como texto UPPER_SNAKE (<c>EnCurso</c> → <c>EN_CURSO</c>), igual que el JSON del API.</summary>
public class ConvertidorEnum<TEnum>() : ValueConverter<TEnum, string>(
    v => NombreEnum.Texto(v),
    v => NombreEnum.Desde<TEnum>(v))
    where TEnum : struct, Enum;

public static class NombreEnum
{
    public static string Texto<TEnum>(TEnum valor) where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseUpper.ConvertName(valor.ToString());

    public static TEnum Desde<TEnum>(string texto) where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().First(v => Texto(v) == texto);
}
