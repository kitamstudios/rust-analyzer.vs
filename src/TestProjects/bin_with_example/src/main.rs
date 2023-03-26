use std;

#[cfg(foo)]
fn needs_foo_to_be_bar() {
}

fn main() {
    needs_foo_to_be_bar();
    println!("Hello, world!");

    println!("A B = {}", std::env::var_os("A B").unwrap_or(std::ffi::OsString::from("NOT FOUND!")).to_str().unwrap());
    println!("XX = {}", std::env::var("XX").unwrap_or("NOT FOUND!".to_string()));
    println!("A = {}", std::env::var("A").unwrap_or("NOT FOUND!".to_string()));

    let args: Vec<String> = std::env::args().collect();
    println!("command line: {:?}", args);

    let mut user_input = String::new();
    std::io::stdin().read_line(&mut user_input);
}

pub fn add(left: usize, right: usize) -> usize {
    left + right
}

pub fn fibonacci(n: u32) -> u32 {
    n
}

#[cfg(test)]
mod tests {
    #[test]
    #[cfg(foo)]
    fn test_requires_foo() {
        assert_eq!(1, 2, "values don't match");
    }
}
