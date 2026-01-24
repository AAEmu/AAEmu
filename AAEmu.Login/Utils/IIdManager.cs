namespace AAEmu.Login.Utils;

public interface IIdManager<T>
{
    /// <summary>
    /// Rents a unique identifier.
    /// </summary>
    /// <remarks>
    /// After use the identifier must be returned using the <see cref="Return"/> method, otherwise the available
    /// identifiers will eventually be exhausted.
    /// </remarks>
    /// <returns>A unique identifier.</returns>
    /// <exception cref="InvalidOperationException">The available identifiers have been exhausted.</exception>
    T Rent();

    /// <summary>
    /// Returns a previously rented identifier.
    /// </summary>
    /// <param name="id">The identifier that was previously rented.</param>
    void Return(T id);
}
